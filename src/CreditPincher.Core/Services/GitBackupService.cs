using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Thin wrapper around the native <c>git</c> command line tool, used to back the
    /// storage directory up to a remote repository and to pull in usage logged on
    /// other machines.
    /// </summary>
    public sealed class GitBackupService
    {
        /// <summary>Git porcelain status codes that indicate an unmerged (conflicted) path.</summary>
        private static readonly HashSet<string> UnmergedStatusCodes =
            new HashSet<string>(StringComparer.Ordinal) { "DD", "AU", "UD", "UA", "DU", "AA", "UU" };

        private readonly string _workingDirectory;
        private readonly IGitConflictResolver _conflictResolver;

        public GitBackupService(string workingDirectory)
            : this(workingDirectory, null)
        {
        }

        public GitBackupService(string workingDirectory, IGitConflictResolver conflictResolver)
        {
            _workingDirectory = workingDirectory;
            _conflictResolver = conflictResolver ?? new NoOpGitConflictResolver();
        }

        /// <summary>The outcome of one git operation.</summary>
        public struct GitResult
        {
            private readonly bool _success;
            private readonly string _output;
            private readonly bool _conflict;

            /// <param name="success">Whether the operation ultimately succeeded.</param>
            /// <param name="output">Human readable log of what happened, suitable for display.</param>
            public GitResult(bool success, string output)
                : this(success, output, false)
            {
            }

            /// <param name="success">Whether the operation ultimately succeeded.</param>
            /// <param name="output">Human readable log of what happened, suitable for display.</param>
            /// <param name="conflict">
            /// True when the failure was an unresolved merge conflict, as opposed to a
            /// network/auth/other git error.
            /// </param>
            public GitResult(bool success, string output, bool conflict)
            {
                _success = success;
                _output = output;
                _conflict = conflict;
            }

            public bool Success { get { return _success; } }

            public string Output { get { return _output; } }

            public bool Conflict { get { return _conflict; } }
        }

        public string WorkingDirectory
        {
            get { return _workingDirectory; }
        }

        /// <summary>Returns true if the git executable can be located and invoked.</summary>
        public bool IsGitAvailable()
        {
            return Run("--version").Success;
        }

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
        public string GetRemoteUrl()
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

            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(result.Output.Trim(), out parsed) ? parsed : (DateTimeOffset?)null;
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

            var pushResult = CommitAndPush("Initial CreditPincher backup", true, null);
            outputs.Add(pushResult.Output);
            return new GitResult(pushResult.Success, Join(outputs), pushResult.Conflict);
        }

        public GitResult CommitAndPush()
        {
            return CommitAndPush("Update CreditPincher log", false, null);
        }

        public GitResult CommitAndPush(Action<string> onStatusUpdate)
        {
            return CommitAndPush("Update CreditPincher log", false, onStatusUpdate);
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
        public GitResult CommitAndPush(string commitMessage, bool initialPush, Action<string> onStatusUpdate)
        {
            if (onStatusUpdate == null)
            {
                onStatusUpdate = _ => { };
            }

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

            var pushArgs = initialPush
                ? new[] { "push", "-u", "origin", "main" }
                : new[] { "push" };

            var pushResult = Run(pushArgs);
            outputs.Add(pushResult.Output);

            if (!pushResult.Success && IsLikelyDivergedRejection(pushResult.Output))
            {
                var reconciliation = ReconcileWithRemote(onStatusUpdate);
                outputs.Add(reconciliation.Output);

                if (!reconciliation.Success)
                {
                    return new GitResult(false, Join(outputs), true);
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

        public GitResult Pull()
        {
            return Pull(null);
        }

        /// <summary>
        /// Pulls remote changes without pushing. Used by the periodic sync so usage logged
        /// on another machine shows up here.
        /// </summary>
        public GitResult Pull(Action<string> onStatusUpdate)
        {
            if (onStatusUpdate == null)
            {
                onStatusUpdate = _ => { };
            }

            var stash = Run("stash", "push", "--include-untracked", "-m", "CreditPincher autosync");
            var stashed = stash.Success &&
                          stash.Output.IndexOf("No local changes", StringComparison.OrdinalIgnoreCase) < 0;

            var pull = Run("pull", "--no-rebase", "--no-edit");
            var outputs = new List<string> { pull.Output };

            if (stashed)
            {
                var pop = Run("stash", "pop");
                outputs.Add(pop.Output);

                if (HasUnresolvedConflicts() && !TryResolveWithConflictResolver(onStatusUpdate, "merge"))
                {
                    return new GitResult(false, Join(outputs), true);
                }
            }

            return new GitResult(pull.Success, Join(outputs));
        }

        /// <summary>Reads one stage of a conflicted file (2 = ours/local, 3 = theirs/remote).</summary>
        public string Show(int stage, string file)
        {
            var result = Run(TimeSpan.FromSeconds(10), "show", ":" + stage + ":" + file);
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

            File.WriteAllText(path, content, new UTF8Encoding(false));
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
            return new GitResult(false, Join(attempts), true);
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

            onStatusUpdate("Conflicts detected in " + conflictedFiles.Count + " file(s); asking conflict resolver…");
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
                .Any(line => line.Length >= 2 && UnmergedStatusCodes.Contains(line.Substring(0, 2)));
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

        private GitResult Run(params string[] args)
        {
            return Run(TimeSpan.FromSeconds(30), args);
        }

        private GitResult Run(TimeSpan timeout, params string[] args)
        {
            try
            {
                var startInfo = new ProcessStartInfo("git")
                {
                    // .NET Framework has no ArgumentList, so the command line is built and
                    // quoted here rather than argument by argument.
                    Arguments = BuildArguments(args),
                    WorkingDirectory = _workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                // Never let git stop for an interactive credential or editor prompt: a tray
                // app has no console to answer one, and the process would hang until timeout.
                startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                startInfo.EnvironmentVariables["GIT_EDITOR"] = "true";
                startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "never";

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new GitResult(false, "Failed to start git.");
                    }

                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        TryKill(process);
                        return new GitResult(false, "git " + string.Join(" ", args) + " timed out.");
                    }

                    var text = string.Join("\n", new[] { stdout.Result, stderr.Result }
                        .Where(part => !string.IsNullOrWhiteSpace(part))
                        .Select(part => part.Trim()));

                    return new GitResult(process.ExitCode == 0, text.Trim());
                }
            }
            catch (Exception exception)
            {
                return new GitResult(false, exception.Message);
            }
        }

        /// <summary>
        /// Joins arguments into a Windows command line, quoting the ones that need it.
        /// Commit messages, remote URLs and <c>:2:usage-log.csv</c> paths can all contain
        /// spaces or quotes, so this follows the usual backslash-before-quote rules.
        /// </summary>
        private static string BuildArguments(IEnumerable<string> args)
        {
            var builder = new StringBuilder();

            foreach (var argument in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0)
                {
                    builder.Append(argument);
                    continue;
                }

                builder.Append('"');

                for (var index = 0; index < argument.Length; index++)
                {
                    var backslashes = 0;
                    while (index < argument.Length && argument[index] == '\\')
                    {
                        backslashes++;
                        index++;
                    }

                    if (index == argument.Length)
                    {
                        // Trailing backslashes must be doubled so they do not escape the
                        // closing quote.
                        builder.Append('\\', backslashes * 2);
                        break;
                    }

                    if (argument[index] == '"')
                    {
                        builder.Append('\\', backslashes * 2 + 1).Append('"');
                    }
                    else
                    {
                        builder.Append('\\', backslashes).Append(argument[index]);
                    }
                }

                builder.Append('"');
            }

            return builder.ToString();
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Nothing useful to do if the process already exited.
            }
        }

        private static string Join(IEnumerable<string> outputs)
        {
            return string.Join("\n", outputs.Where(output => !string.IsNullOrWhiteSpace(output))).Trim();
        }
    }
}
