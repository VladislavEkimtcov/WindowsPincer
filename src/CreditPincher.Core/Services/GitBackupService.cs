using System.Diagnostics;
using System.Text;

namespace CreditPincher.Core.Services;

/// <summary>
/// Thin wrapper around the native <c>git</c> command line tool, used to back the
/// storage directory up to a remote repository and to pull in usage logged on
/// other machines.
/// </summary>
public sealed class GitBackupService
{
    /// <summary>Git porcelain status codes that indicate an unmerged (conflicted) path.</summary>
    private static readonly HashSet<string> UnmergedStatusCodes =
        new(StringComparer.Ordinal) { "DD", "AU", "UD", "UA", "DU", "AA", "UU" };

    private readonly string _workingDirectory;
    private readonly IGitConflictResolver _conflictResolver;

    public GitBackupService(string workingDirectory, IGitConflictResolver? conflictResolver = null)
    {
        _workingDirectory = workingDirectory;
        _conflictResolver = conflictResolver ?? new NoOpGitConflictResolver();
    }

    /// <param name="Success">Whether the operation ultimately succeeded.</param>
    /// <param name="Output">Human readable log of what happened, suitable for display.</param>
    /// <param name="Conflict">
    /// True when the failure was an unresolved merge conflict, as opposed to a
    /// network/auth/other git error.
    /// </param>
    public readonly record struct GitResult(bool Success, string Output, bool Conflict = false);

    public string WorkingDirectory => _workingDirectory;

    /// <summary>Returns true if the git executable can be located and invoked.</summary>
    public bool IsGitAvailable() => Run("--version").Success;

    /// <summary>Returns true if the storage directory is already inside a git work tree.</summary>
    public bool IsGitRepository()
    {
        if (!IsGitAvailable())
        {
            return false;
        }

        var result = Run("rev-parse", "--is-inside-work-tree");
        return result.Success && result.Output.Trim() == "true";
    }

    /// <summary>The configured <c>origin</c> URL, or null when there is no repo/remote.</summary>
    public string? GetRemoteUrl()
    {
        var result = Run("remote", "get-url", "origin");
        return result.Success && result.Output.Trim().Length > 0 ? result.Output.Trim() : null;
    }

    /// <summary>Timestamp of the most recent commit, for the "last backed up" readout.</summary>
    public DateTimeOffset? GetLastCommitTime()
    {
        var result = Run("log", "-1", "--format=%cI");
        if (!result.Success || result.Output.Trim().Length == 0)
        {
            return null;
        }

        return DateTimeOffset.TryParse(result.Output.Trim(), out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Initializes a repository in the storage directory, wires it to
    /// <paramref name="remoteUrl"/> as <c>origin</c>, then commits and pushes.
    /// </summary>
    public GitResult ConnectToRemote(string remoteUrl)
    {
        if (!IsGitAvailable())
        {
            return new GitResult(false, "git executable not found on PATH.");
        }

        var outputs = new List<string>();

        if (!IsGitRepository())
        {
            var initResult = Run("init");
            outputs.Add(initResult.Output);
            if (!initResult.Success)
            {
                return new GitResult(false, Join(outputs));
            }
        }

        outputs.Add(Run("branch", "-M", "main").Output);

        // Replace any existing origin remote so re-connecting works as expected.
        Run("remote", "remove", "origin");
        var remoteResult = Run("remote", "add", "origin", remoteUrl);
        outputs.Add(remoteResult.Output);
        if (!remoteResult.Success)
        {
            return new GitResult(false, Join(outputs));
        }

        var pushResult = CommitAndPush("Initial CreditPincher backup", initialPush: true);
        outputs.Add(pushResult.Output);
        return new GitResult(pushResult.Success, Join(outputs), pushResult.Conflict);
    }

    /// <summary>
    /// Stages all changes, commits (when there is anything to commit), and pushes to origin.
    ///
    /// If the push is rejected because the remote diverged, this reconciles first via a
    /// merge, then via a rebase, and retries the push once. If neither automatic strategy
    /// works, the conflict resolver gets a chance; if that also fails, the in-progress
    /// merge/rebase is aborted and a failed result with <c>Conflict = true</c> is returned.
    ///
    /// <paramref name="onStatusUpdate"/> is invoked on the calling thread; callers running
    /// this in the background marshal back to the UI thread themselves.
    /// </summary>
    public GitResult CommitAndPush(
        string commitMessage = "Update CreditPincher log",
        bool initialPush = false,
        Action<string>? onStatusUpdate = null)
    {
        onStatusUpdate ??= _ => { };
        var outputs = new List<string>();

        var addResult = Run("add", "-A");
        outputs.Add(addResult.Output);
        if (!addResult.Success)
        {
            return new GitResult(false, Join(outputs));
        }

        var statusResult = Run("status", "--porcelain");
        var hasChangesToCommit = !string.IsNullOrWhiteSpace(statusResult.Output);

        if (hasChangesToCommit)
        {
            var commitResult = Run("commit", "-m", commitMessage);
            outputs.Add(commitResult.Output);
            if (!commitResult.Success)
            {
                return new GitResult(false, Join(outputs));
            }
        }

        string[] pushArgs = initialPush ? ["push", "-u", "origin", "main"] : ["push"];
        var pushResult = Run(pushArgs);
        outputs.Add(pushResult.Output);

        if (!pushResult.Success && IsLikelyDivergedRejection(pushResult.Output))
        {
            var reconciliation = ReconcileWithRemote(onStatusUpdate);
            outputs.Add(reconciliation.Output);

            if (!reconciliation.Success)
            {
                return new GitResult(false, Join(outputs), Conflict: true);
            }

            pushResult = Run(pushArgs);
            outputs.Add(pushResult.Output);
        }

        if (!hasChangesToCommit && pushResult.Success)
        {
            outputs.Add("Nothing new to commit; pushed existing state.");
        }

        return new GitResult(pushResult.Success, Join(outputs));
    }

    /// <summary>
    /// Pulls remote changes without pushing. Used by the periodic sync so usage logged
    /// on another machine shows up here.
    /// </summary>
    public GitResult Pull(Action<string>? onStatusUpdate = null)
    {
        onStatusUpdate ??= _ => { };

        var stash = Run("stash", "push", "--include-untracked", "-m", "CreditPincher autosync");
        var stashed = stash.Success && !stash.Output.Contains("No local changes", StringComparison.OrdinalIgnoreCase);

        var pull = Run("pull", "--no-rebase", "--no-edit");
        var outputs = new List<string> { pull.Output };

        if (stashed)
        {
            var pop = Run("stash", "pop");
            outputs.Add(pop.Output);

            if (HasUnresolvedConflicts() && !TryResolveWithConflictResolver(onStatusUpdate, "merge"))
            {
                return new GitResult(false, Join(outputs), Conflict: true);
            }
        }

        return new GitResult(pull.Success, Join(outputs));
    }

    /// <summary>Reads one stage of a conflicted file (2 = ours/local, 3 = theirs/remote).</summary>
    public string Show(int stage, string file)
    {
        var result = Run(TimeSpan.FromSeconds(10), "show", $":{stage}:{file}");
        return result.Success ? result.Output : string.Empty;
    }

    /// <summary>Writes resolved content for a conflicted file and stages it.</summary>
    public void WriteResolvedFile(string file, string content)
    {
        var path = Path.Combine(_workingDirectory, file);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Returns the file paths that currently have merge conflicts.</summary>
    public IReadOnlyList<string> ConflictedFiles()
    {
        var result = Run("diff", "--name-only", "--diff-filter=U");
        return result.Output
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Brings the local branch up to date with its remote counterpart without user
    /// interaction where possible: merge first, then rebase, then the conflict resolver.
    /// Whatever is left in progress is aborted so the working tree stays clean.
    /// </summary>
    private GitResult ReconcileWithRemote(Action<string> onStatusUpdate)
    {
        var attempts = new List<string>();

        onStatusUpdate("Remote has new changes; attempting to merge automatically…");
        var mergeResult = Run("pull", "--no-rebase", "--no-edit");
        attempts.Add(mergeResult.Output);
        if (mergeResult.Success && !HasUnresolvedConflicts())
        {
            return new GitResult(true, Join(attempts));
        }

        if (HasUnresolvedConflicts() && TryResolveWithConflictResolver(onStatusUpdate, "merge"))
        {
            return new GitResult(true, Join(attempts));
        }

        Run("merge", "--abort");

        onStatusUpdate("Automatic merge failed; retrying with a rebase…");
        var rebaseResult = Run("pull", "--rebase");
        attempts.Add(rebaseResult.Output);
        if (rebaseResult.Success && !HasUnresolvedConflicts())
        {
            return new GitResult(true, Join(attempts));
        }

        if (HasUnresolvedConflicts() && TryResolveWithConflictResolver(onStatusUpdate, "rebase"))
        {
            return new GitResult(true, Join(attempts));
        }

        Run("rebase", "--abort");

        onStatusUpdate("Automatic conflict resolution failed.");
        attempts.Add("Could not automatically reconcile with the remote branch; manual conflict resolution is required.");
        return new GitResult(false, Join(attempts), Conflict: true);
    }

    /// <summary>
    /// Lets the resolver deal with conflicts left by an in-progress merge or rebase,
    /// then finalizes that operation (commit for a merge, <c>--continue</c> for a rebase).
    /// </summary>
    private bool TryResolveWithConflictResolver(Action<string> onStatusUpdate, string operation)
    {
        var conflictedFiles = ConflictedFiles();
        if (conflictedFiles.Count == 0)
        {
            return false;
        }

        onStatusUpdate($"Conflicts detected in {conflictedFiles.Count} file(s); asking conflict resolver…");
        if (!_conflictResolver.ResolveConflicts(this, conflictedFiles))
        {
            return false;
        }

        Run("add", "-A");
        var finalizeResult = operation == "rebase"
            ? Run("rebase", "--continue")
            : Run("commit", "--no-edit");

        return finalizeResult.Success && !HasUnresolvedConflicts();
    }

    /// <summary>Returns true if <c>git status --porcelain</c> reports any unmerged paths.</summary>
    private bool HasUnresolvedConflicts()
    {
        var status = Run("status", "--porcelain");
        return status.Output
            .Split('\n')
            .Any(line => line.Length >= 2 && UnmergedStatusCodes.Contains(line[..2]));
    }

    /// <summary>Heuristic check for a push rejection caused by the remote having diverged.</summary>
    private static bool IsLikelyDivergedRejection(string output)
    {
        var lower = output.ToLowerInvariant();
        return lower.Contains("rejected") ||
               lower.Contains("non-fast-forward") ||
               lower.Contains("fetch first") ||
               lower.Contains("failed to push");
    }

    private GitResult Run(params string[] args) => Run(TimeSpan.FromSeconds(30), args);

    private GitResult Run(TimeSpan timeout, params string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            // Never let git stop for an interactive credential or editor prompt: a tray
            // app has no console to answer one, and the process would hang until timeout.
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GIT_EDITOR"] = "true";
            startInfo.Environment["GCM_INTERACTIVE"] = "never";

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new GitResult(false, "Failed to start git.");
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                TryKill(process);
                return new GitResult(false, $"git {string.Join(' ', args)} timed out.");
            }

            var text = string.Join('\n', new[] { stdout.Result, stderr.Result }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));

            return new GitResult(process.ExitCode == 0, text.Trim());
        }
        catch (Exception exception)
        {
            return new GitResult(false, exception.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Nothing useful to do if the process already exited.
        }
    }

    private static string Join(IEnumerable<string> outputs) =>
        string.Join('\n', outputs.Where(output => !string.IsNullOrWhiteSpace(output))).Trim();
}
