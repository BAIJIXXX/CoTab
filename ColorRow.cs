using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CoTab.Helpers;

namespace CoTab;

/// <summary>A compact "label + colour swatch + hex" row used by the settings panel.</summary>
public sealed class ColorRow : ContentControl
{
    private readonly Border _swatch;
    private readonly TextBlock _hex;
    private readonly Border _clickable;
    private Color _color;

    public ColorRow(string label, Color initial, bool withAlpha = false)
    {
        _color = initial;
        WithAlpha = withAlpha;
        Focusable = false;
        Margin = new Thickness(0, 1, 0, 1);

        var grid = new Grid { Height = 28 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "SettingsTextBrush");
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        _swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(initial),
            VerticalAlignment = VerticalAlignment.Center
        };
        _swatch.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");

        _hex = new TextBlock
        {
            Text = ColorUtil.ToHex(initial),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Width = 58,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        };
        _hex.SetResourceReference(TextBlock.ForegroundProperty, "SettingsMutedBrush");

        var pencil = new Path
        {
            Data = Geometry.Parse("M2 11.2 L9.4 3.8 L12.2 6.6 L4.8 14 L1.4 14.4 Z M8.4 4.8 L11.2 7.6"),
            StrokeThickness = 1.1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 13,
            Height = 15,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 1, 0)
        };
        pencil.SetResourceReference(Shape.StrokeProperty, "SettingsMutedBrush");

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_swatch);
        content.Children.Add(_hex);
        content.Children.Add(pencil);

        _clickable = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 0, 5, 0),
            Child = content,
            Cursor = Cursors.Hand,
            ToolTip = "点击选择颜色"
        };
        _clickable.MouseLeftButtonUp += Clickable_MouseLeftButtonUp;
        _clickable.MouseEnter += (_, _) => _clickable.SetResourceReference(Border.BackgroundProperty, "SettingsHoverBrush");
        _clickable.MouseLeave += (_, _) => _clickable.Background = Brushes.Transparent;

        Grid.SetColumn(_clickable, 1);
        grid.Children.Add(_clickable);
        Content = grid;
    }

    public bool WithAlpha { get; }

    public string Label
    {
        get => ((TextBlock)((Grid)Content).Children[0]).Text;
        set => ((TextBlock)((Grid)Content).Children[0]).Text = value;
    }

    public Color Color
    {
        get => _color;
        set => SetColor(value, false);
    }

    public event EventHandler? ColorChanged;

    public void SetColor(Color color, bool raise)
    {
        var visible = WithAlpha ? color : Color.FromRgb(color.R, color.G, color.B);
        _color = visible;
        _swatch.Background = new SolidColorBrush(visible);
        _hex.Text = WithAlpha ? ColorUtil.ToHexArgb(visible) : ColorUtil.ToHex(visible);
        if (raise) ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Clickable_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
        var dialog = new ColorPickerWindow(_color) { Owner = owner };
        if (dialog.ShowDialog() == true)
            SetColor(dialog.SelectedColor, true);
    }
}
