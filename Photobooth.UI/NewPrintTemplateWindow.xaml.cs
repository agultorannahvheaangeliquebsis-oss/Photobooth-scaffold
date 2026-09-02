using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Core;

namespace Photobooth.UI;

/// <summary>"New Template" dialog for PrintTemplateEditorWindow's switcher: a name
/// plus a gallery of PrintTemplatePresets to start from instead of always a blank
/// canvas. Rows are built in code, same dynamic-rows-from-a-list approach AdminWindow
/// already uses for its Frame/Guestbook/Attendant sections, rather than an
/// ItemsControl+DataTemplate for what's a short, fixed gallery.</summary>
public partial class NewPrintTemplateWindow : Window
{
    public string TemplateName { get; private set; } = "";
    public PrintTemplatePreset SelectedPreset { get; private set; } = PrintTemplatePresets.Blank;

    private readonly Dictionary<Button, PrintTemplatePreset> _presetByButton = new();

    public NewPrintTemplateWindow(string defaultName)
    {
        InitializeComponent();
        NameBox.Text = defaultName;

        foreach (PrintTemplatePreset preset in PrintTemplatePresets.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("InkBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 220,
                TextWrapping = TextWrapping.Wrap,
            });
            row.Children.Add(new TextBlock
            {
                Text = preset == PrintTemplatePresets.Blank
                    ? "Start empty"
                    : $"{preset.RequiredPhotoCount} photo slot{(preset.RequiredPhotoCount == 1 ? "" : "s")} · {preset.WidthInches:0.#}×{preset.HeightInches:0.#}\"",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button { Style = (Style)FindResource("PresetRowButton"), Content = row };
            button.Click += PresetButton_Click;
            _presetByButton[button] = preset;
            PresetListPanel.Children.Add(button);
        }

        SelectPreset(_presetByButton.First(kv => kv.Value == PrintTemplatePresets.Blank).Key);
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && _presetByButton.ContainsKey(button))
        {
            SelectPreset(button);
        }
    }

    /// <summary>Tag is purely a visual "am I the selected row" marker for
    /// PresetRowButton's style trigger -- the preset each button represents is
    /// tracked separately in _presetByButton, not round-tripped through Tag.</summary>
    private void SelectPreset(Button selectedButton)
    {
        SelectedPreset = _presetByButton[selectedButton];
        foreach (Button button in _presetByButton.Keys)
        {
            button.Tag = button == selectedButton ? "Selected" : null;
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            NameBox.Text = "Untitled";
            name = "Untitled";
        }

        TemplateName = name;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
