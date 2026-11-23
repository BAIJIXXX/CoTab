using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoTab.Helpers;

namespace CoTab;

public partial class ColorPickerWindow : Window
{
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#F4F5F7", "#D9DCE3", "#AEB4BF", "#7B8290", "#555B66", "#33363D", "#1E1F23", "#000000",
        "#FFF6C9", "#FFF1B8", "#FFE191", "#FFB3A7", "#FF8E7A", "#F7C7E8", "#E7BDFF", "#C9C4FF", "#A9C6FF",
        "#BFE9FF", "#B9F1E6", "#BDF3C8", "#DDF7A8", "#F7D9A8", "#E8C7A9", "#BFA28A", "#E7A6B4", "#C98A9A"
    };

    private bool _updating;
    private Color _color;

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        BuildPalette();
        SetColor(initial);
    }

    public Color SelectedColor => _color;

    private void BuildPalette()
    {
        foreach (var hex in Palette)
        {
            var color = ColorUtil.Parse(hex);
            var button = new Button
            {
                Style = (Style)FindResource("PaletteSwatchStyle"),
                Background = new SolidColorBrush(color),
                ToolTip = hex
            };
            button.Click += (_, _) => SetColor(color);
            PaletteGrid.Children.Add(button);
        }
    }

    private void SetColor(Color color, bool updateHexBox = true)
    {
        _updating = true;
        _color = Color.FromRgb(color.R, color.G, color.B);
        RedSlider.Value = _color.R;
        GreenSlider.Value = _color.G;
        BlueSlider.Value = _color.B;
        RedValue.Text = _color.R.ToString();
        GreenValue.Text = _color.G.ToString();
        BlueValue.Text = _color.B.ToString();
        PreviewBorder.Background = new SolidColorBrush(_color);
        PreviewHexText.Text = $"{ColorUtil.ToHex(_color)}   RGB {_color.R}, {_color.G}, {_color.B}";
        if (updateHexBox) HexBox.Text = ColorUtil.ToHex(_color);
        _updating = false;
    }

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        var color = Color.FromRgb((byte)Math.Round(RedSlider.Value), (byte)Math.Round(GreenSlider.Value), (byte)Math.Round(BlueSlider.Value));
        SetColor(color);
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var text = HexBox.Text.Trim();
        if (!ColorUtil.TryParseHex(text, out var color)) return;
        SetColor(color, updateHexBox: false);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HeaderRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse was released immediately.
        }
    }
}
