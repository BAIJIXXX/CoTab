using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CoTab;

/// <summary>Small dark modal used for naming a board.</summary>
public sealed class TextPromptWindow : Window
{
    private readonly TextBox _input;

    public string Value { get; private set; }

    private TextPromptWindow(string title, string label, string initial)
    {
        Value = initial;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 316;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFA, 0x17, 0x18, 0x1C)),
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        };

        var root = new StackPanel { Margin = new Thickness(20, 17, 20, 18) };

        root.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xD6, 0xD9, 0xE0)),
            FontSize = 11.5,
            Margin = new Thickness(0, 9, 0, 7)
        });

        _input = new TextBox
        {
            Text = initial,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6),
            FontSize = 12.5,
            CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD6, 0x6B))
        };
        _input.KeyDown += Input_KeyDown;
        root.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };

        var cancel = new Button
        {
            Content = "取消",
            Padding = new Thickness(15, 5, 15, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource("SettingsButtonStyle") as Style ?? TryFindResource("SettingsSecondaryButton") as Style
        };
        cancel.Click += (_, _) => DialogResult = false;

        var ok = new Button
        {
            Content = "确定",
            Padding = new Thickness(16, 5, 16, 5),
            Style = TryFindResource("AccentButtonStyle") as Style
        };
        ok.Click += (_, _) => Accept();

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        card.Child = root;
        Content = card;

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Accept()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        Value = text.Length > 30 ? text[..30] : text;
        DialogResult = true;
    }

    public static string? Show(Window owner, string title, string label, string initial)
    {
        var dialog = new TextPromptWindow(title, label, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
