using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace CreditPincher.App.Views;

/// <summary>
/// The fast path: a single box that takes a credit amount and gets out of the way.
/// Opened from the tray menu or the global hotkey.
/// </summary>
public partial class QuickLogWindow : Window
{
    private readonly AppServices _services;

    public QuickLogWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            RefreshContext();
            AmountBox.Focus();
            AmountBox.SelectAll();
        };

        Activated += (_, _) =>
        {
            RefreshContext();
            AmountBox.Focus();
        };

        // Behave like a popup: clicking away dismisses it.
        Deactivated += (_, _) => Close();
    }

    private void RefreshContext()
    {
        try
        {
            var monthToDate = _services.MonthToDate();
            var formatter = _services.Formatter;
            ContextText.Text = $"{formatter.Credits(monthToDate.TotalCredits)} logged this month";
        }
        catch (Exception)
        {
            ContextText.Text = string.Empty;
        }

        StatusText.Text = string.Empty;
    }

    private void OnAmountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnLogClick(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        var text = AmountBox.Text.Trim();

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var amount) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
        {
            StatusText.Text = "Enter a positive number of credits.";
            return;
        }

        if (!double.IsFinite(amount) || amount <= 0)
        {
            StatusText.Text = "Enter a positive number of credits.";
            return;
        }

        try
        {
            _services.Storage.AddUsage(amount);
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        _services.RaiseDataChanged();
        Close();
    }
}
