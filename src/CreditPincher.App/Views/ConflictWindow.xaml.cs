using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CreditPincher.App.Theming;
using CreditPincher.Core.Services;

namespace CreditPincher.App.Views
{
    /// <summary>
    /// Lets the user pick a resolution per conflicted file when git could not merge on
    /// its own. For <c>usage-log.csv</c> the recommended answer is a chronological merge:
    /// two machines logging into one synced folder is the expected case, and dropping
    /// either side would lose real usage.
    /// </summary>
    public partial class ConflictWindow : Window
    {
        private const string UsageLogFile = "usage-log.csv";
        private const string BudgetFile = "monthly-budget.txt";

        private readonly GitBackupService _git;
        private readonly IReadOnlyList<string> _conflictedFiles;

        private readonly Dictionary<string, Func<string>> _selections =
            new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase);

        public ConflictWindow(GitBackupService git, IReadOnlyList<string> conflictedFiles)
        {
            _git = git;
            _conflictedFiles = conflictedFiles;
            InitializeComponent();
            TitleBarTheme.Attach(this);
            BuildFileCards();
        }

        /// <summary>True when the user resolved everything and the caller may continue.</summary>
        public bool Resolved { get; private set; }

        private void BuildFileCards()
        {
            foreach (var file in _conflictedFiles)
            {
                var options = new StackPanel();
                var groupName = "group_" + Guid.NewGuid().ToString("N");
                var currentFile = file;

                if (string.Equals(file, UsageLogFile, StringComparison.OrdinalIgnoreCase))
                {
                    AddOption(options, currentFile, groupName, "Merge every entry, oldest first (recommended)", true,
                        () => UsageLogMerger.Merge(_git.Show(2, currentFile), _git.Show(3, currentFile)));
                    AddOption(options, currentFile, groupName, "Keep only the entries logged on this machine", false,
                        () => _git.Show(2, currentFile));
                    AddOption(options, currentFile, groupName, "Keep only the entries from the remote", false,
                        () => _git.Show(3, currentFile));
                }
                else if (string.Equals(file, BudgetFile, StringComparison.OrdinalIgnoreCase))
                {
                    var local = _git.Show(2, currentFile).Trim();
                    var remote = _git.Show(3, currentFile).Trim();

                    AddOption(options, currentFile, groupName,
                        "Keep the budget set here (" + Describe(local) + ")", true,
                        () => _git.Show(2, currentFile));
                    AddOption(options, currentFile, groupName,
                        "Keep the remote budget (" + Describe(remote) + ")", false,
                        () => _git.Show(3, currentFile));
                }
                else
                {
                    AddOption(options, currentFile, groupName, "Keep the local version", true,
                        () => _git.Show(2, currentFile));
                    AddOption(options, currentFile, groupName, "Keep the remote version", false,
                        () => _git.Show(3, currentFile));
                }

                var heading = new TextBlock
                {
                    Text = file,
                    Style = (Style)FindResource("HeadingText"),
                };

                var content = new StackPanel();
                content.Children.Add(heading);
                content.Children.Add(options);

                var card = new Border
                {
                    Style = (Style)FindResource("Card"),
                    Child = content,
                };

                FilesPanel.Children.Add(card);
            }
        }

        private void AddOption(
            Panel container,
            string file,
            string groupName,
            string label,
            bool isChecked,
            Func<string> resolve)
        {
            var radio = new RadioButton
            {
                Content = label,
                GroupName = groupName,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 6),
            };

            if (isChecked)
            {
                _selections[file] = resolve;
            }

            radio.Checked += (sender, args) => _selections[file] = resolve;
            container.Children.Add(radio);
        }

        private static string Describe(string value)
        {
            return value.Length > 0 ? value : "not set";
        }

        private void OnResolveClick(object sender, RoutedEventArgs e)
        {
            ResolveButton.IsEnabled = false;

            try
            {
                foreach (var file in _conflictedFiles)
                {
                    Func<string> resolve;
                    if (_selections.TryGetValue(file, out resolve))
                    {
                        _git.WriteResolvedFile(file, resolve());
                    }
                }

                Resolved = true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    "Could not write the resolved files:\n\n" + exception.Message,
                    "CreditPincher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Resolved = false;
            }

            ResolveButton.IsEnabled = true;
            DialogResult = Resolved;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Resolved = false;
            DialogResult = false;
        }
    }
}
