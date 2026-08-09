using System.Windows;
using System.Windows.Threading;
using CreditPincher.App.Views;
using CreditPincher.Core.Services;

namespace CreditPincher.App.Services;

/// <summary>
/// Shows <see cref="ConflictWindow"/> when git needs a human decision. Backups run on
/// a worker thread, so this marshals to the UI thread and blocks that worker until the
/// user answers — which is exactly what <see cref="GitBackupService"/> expects.
/// </summary>
public sealed class WpfGitConflictResolver : IGitConflictResolver
{
    private readonly Dispatcher _dispatcher;

    public WpfGitConflictResolver(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool ResolveConflicts(GitBackupService git, IReadOnlyList<string> conflictedFiles)
    {
        if (conflictedFiles.Count == 0)
        {
            return false;
        }

        return _dispatcher.Invoke(() =>
        {
            var window = new ConflictWindow(git, conflictedFiles)
            {
                Owner = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(candidate => candidate.IsVisible),
            };

            window.ShowDialog();
            return window.Resolved;
        });
    }
}
