using System.Windows;
using System.Windows.Input;

namespace Photobooth.UI;

/// <summary>
/// Minimal reusable "type a name" modal -- WPF has no built-in InputBox, and
/// EventLauncherWindow needs the same one for New event/Rename/Duplicate, so
/// it's its own small window rather than three copies of a MessageBox-adjacent
/// hack. Set <see cref="DialogResult"/> is true and <see cref="Value"/> holds
/// the trimmed text on OK.
/// </summary>
public partial class TextPromptWindow : Window
{
    public string Value { get; private set; } = string.Empty;

    public TextPromptWindow(string title, string initialValue = "", string? caption = null)
    {
        InitializeComponent();
        PromptTitleText.Text = title;
        ValueTextBox.Text = initialValue;
        ValueTextBox.SelectAll();
        if (caption is { Length: > 0 })
        {
            PromptCaptionText.Text = caption;
            PromptCaptionText.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => ValueTextBox.Focus();
    }

    private void ValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryAccept();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void TryAccept()
    {
        string trimmed = ValueTextBox.Text.Trim();
        if (trimmed.Length == 0)
        {
            ErrorText.Text = "Enter a name.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Value = trimmed;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
