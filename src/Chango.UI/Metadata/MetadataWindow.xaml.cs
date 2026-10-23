using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chango.Core;

namespace Chango.UI.Metadata;

/// <summary>
/// The Info window: what a file is (read-only facts) and its editable details. Saving never touches the original;
/// the caller writes the changes into a copy. <see cref="Result"/> completes with the edit, or null when closed.
/// </summary>
public partial class MetadataWindow : Window
{
    private readonly MetadataSnapshot _snapshot;
    private readonly Dictionary<string, Func<string?>> _readers = [];
    private readonly TaskCompletionSource<MetadataEdit?> _result = new();

    public MetadataWindow(MetadataSnapshot snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;
        Title = $"{Path.GetFileName(snapshot.Path)} · Info";
        FileNameText.Text = Path.GetFileName(snapshot.Path);
        SummaryText.Text = string.Join(" · ", snapshot.Facts.Where(f => f.Key is "Type" or "Size").Select(f => f.Value));
        KindGlyph.Text = snapshot.Kind switch
        {
            "Image" => "", "Audio" => "", "Video" => "", "Document" => "", _ => "",
        };

        int row = 0;
        foreach (var (label, value) in snapshot.Facts.Where(f => f.Key is not ("Type" or "Size")))
        {
            FactsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Style = (Style)FindResource("Chango.Text.Caption"), Margin = new Thickness(0, 4, 12, 3), VerticalAlignment = VerticalAlignment.Top };
            var v = new TextBox
            {
                Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = null, Padding = new Thickness(0),
                Margin = new Thickness(0, 3, 0, 3), MinHeight = 0, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily, FontSize = 13,
                Template = ReadOnlyTemplate(),
                Foreground = (System.Windows.Media.Brush)FindResource("Chango.TextPrimary"),
            };
            Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            FactsGrid.Children.Add(l); FactsGrid.Children.Add(v);
            row++;
        }

        foreach (var field in snapshot.Fields) FieldsPanel.Children.Add(Editor(field));
        EditSection.Visibility = snapshot.Fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LocationCard.Visibility = snapshot.HasLocation ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot.Fields.Count == 0 && !snapshot.HasLocation)
        {
            SaveButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Done";
            FooterHint.Text = "Windows can't edit the details of this kind of file.";
        }
        UpdateSaveState();
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public Task<MetadataEdit?> Result => _result.Task;

    /// <summary>A bare text template for the read-only facts: selectable text without the text-box chrome and padding.</summary>
    private static ControlTemplate ReadOnlyTemplate()
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = host };
    }

    private FrameworkElement Editor(MetadataField field)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = field.Label, Style = (Style)FindResource("FieldLabel") });
        switch (field.Kind)
        {
            case MetadataFieldKind.Rating:
            {
                int current = int.TryParse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0;
                var stars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 0, 0, 0) };
                var buttons = new List<ToggleButton>();
                for (int i = 1; i <= 5; i++)
                {
                    int value = i;
                    var b = new ToggleButton { Style = (Style)FindResource("Star"), IsChecked = i <= current, ToolTip = $"{i} star{(i > 1 ? "s" : "")}" };
                    b.Click += (_, _) =>
                    {
                        current = current == value ? 0 : value; // clicking the current rating clears it
                        for (int k = 0; k < buttons.Count; k++) buttons[k].IsChecked = k < current;
                        UpdateSaveState();
                    };
                    buttons.Add(b);
                    stars.Children.Add(b);
                }
                panel.Children.Add(stars);
                _readers[field.Key] = () => current == 0 ? null : current.ToString(CultureInfo.InvariantCulture);
                break;
            }
            default:
            {
                var box = new TextBox
                {
                    Text = field.Value ?? "", FontSize = 13,
                    AcceptsReturn = field.Kind == MetadataFieldKind.MultiLine,
                    TextWrapping = field.Kind == MetadataFieldKind.MultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = field.Kind == MetadataFieldKind.MultiLine ? 64 : 0,
                    VerticalContentAlignment = field.Kind == MetadataFieldKind.MultiLine ? VerticalAlignment.Top : VerticalAlignment.Center,
                    ToolTip = field.Hint,
                };
                System.Windows.Automation.AutomationProperties.SetName(box, field.Label);
                box.TextChanged += (_, _) => UpdateSaveState();
                panel.Children.Add(box);
                _readers[field.Key] = () => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
                break;
            }
        }
        return panel;
    }

    /// <summary>Only the fields the user actually changed (comparing trimmed text).</summary>
    public MetadataEdit CurrentEdit()
    {
        var changes = new Dictionary<string, string?>();
        foreach (var field in _snapshot.Fields)
        {
            string? now = _readers[field.Key](), before = string.IsNullOrWhiteSpace(field.Value) ? null : field.Value.Trim();
            if (field.Kind == MetadataFieldKind.Rating && before == "0") before = null;
            if (!string.Equals(now, before, StringComparison.Ordinal)) changes[field.Key] = now;
        }
        return new MetadataEdit(changes, RemoveLocationBox.IsChecked == true);
    }

    private void UpdateSaveState() => SaveButton.IsEnabled = !CurrentEdit().IsEmpty;
    private void OnChanged(object sender, RoutedEventArgs e) => UpdateSaveState();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var edit = CurrentEdit();
        // Validate typed numbers and dates here, where the user can still fix them.
        foreach (var field in _snapshot.Fields)
        {
            if (!edit.Changes.TryGetValue(field.Key, out var text) || text is null) continue;
            bool ok = field.Kind switch
            {
                MetadataFieldKind.Number => uint.TryParse(text, NumberStyles.None, CultureInfo.CurrentCulture, out _),
                MetadataFieldKind.Date => DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _),
                _ => true,
            };
            if (!ok)
            {
                MessageBox.Show(this, $"\"{text}\" isn't a valid {field.Label.ToLowerInvariant()}.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        _result.TrySetResult(edit);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
