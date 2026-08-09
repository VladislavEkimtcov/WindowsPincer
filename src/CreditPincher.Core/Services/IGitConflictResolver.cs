using System.Collections.Generic;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Hook for resolving git merge conflicts that could not be resolved
    /// automatically (e.g. via <c>git pull --no-rebase --no-edit</c> or
    /// <c>git pull --rebase</c>).
    ///
    /// Implementations may present UI to the user and must leave the working tree
    /// in a resolved, stageable state when they return <c>true</c>.
    /// </summary>
    public interface IGitConflictResolver
    {
        /// <summary>
        /// Attempts to resolve <paramref name="conflictedFiles"/> inside the repository
        /// driven by <paramref name="git"/>. Returns <c>true</c> if the caller can proceed
        /// (stage, commit / <c>rebase --continue</c>, push), or <c>false</c> if the conflicts
        /// remain and the in-progress merge/rebase should be aborted.
        /// </summary>
        bool ResolveConflicts(GitBackupService git, IReadOnlyList<string> conflictedFiles);
    }

    /// <summary>Declines every conflict, so the backup aborts cleanly and reports it.</summary>
    public sealed class NoOpGitConflictResolver : IGitConflictResolver
    {
        public bool ResolveConflicts(GitBackupService git, IReadOnlyList<string> conflictedFiles)
        {
            return false;
        }
    }
}
