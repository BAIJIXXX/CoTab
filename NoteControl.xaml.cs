using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CoTab.Helpers;
using CoTab.Models;
using CoTab.Services;

namespace CoTab;

public partial class NoteControl : UserControl
{
    private static int _zTop = 1000;

    private static readonly string[] NoteFills =
    {
        "#FFF6C9", "#FFF1B8", "#FFE7A8", "#FDE2C4", "#F7D6D9", "#E6DAF7",
        "#D9E8F7", "#D8EFD9", "#ECE6D8", "#F2F2F2", "#FFFFFF", "#F3C9B6"
    };

    private static readonly string[] InkColors =
    {
        "#3D382B", "#2F3540", "#5B4A2F", "#3F5545", "#384A63",
        "#5A3552", "#7A3A3A", "#1F1F1F", "#FFFFFF"
    };

    private bool _editing;
    private bool _dragging;
    private bool _moved;
    private bool _resizing;
    private Point _dragOrigin;
    private double _dragStartX;
    private double _dragStartY;
    private Point _resizeOrigin;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private ContextMenu? _menu;
    private MenuItem? _plainItem;
    private MenuItem? _markdownItem;

    public NoteControl(NoteModel model)
    {
        Model = model;
        InitializeComponent();

        Width = model.Width >= 140 ? model.Width : 184;
        Height = model.Height >= 110 ? model.Height : 184;
        Panel.SetZIndex(this, ++_zTop);

        BuildContextMenu();
        ApplyColors();
        RenderContent();

        NoteBody.MouseLeftButtonDown += Body_MouseLeftButtonDown;
        NoteBody.MouseMove += Body_MouseMove;
        NoteBody.MouseLeftButtonUp += Body_MouseLeftButtonUp;
        NoteBody.MouseRightButtonUp += Body_MouseRightButtonUp;
        NoteBody.MouseEnter += (_, _) =>
        {
            if (!_editing) HoverRing.Visibility = Visibility.Visible;
            ResizeCorner.Opacity = 0.9;
        };
        NoteBody.MouseLeave += (_, _) =>
        {
            HoverRing.Visibility = Visibility.Collapsed;
            if (!_resizing) ResizeCorner.Opacity = 0;
        };

        ResizeCorner.Opacity = 0;
        SizeChanged += (_, _) => UpdateSizeBadge();
        ContextMenu = _menu;
        ContextMenuOpening += (_, _) => RefreshMenuState();
        // Keep the note selected while it is edited from the keyboard.
        PreviewMouseDown += (_, _) => Activate();
    }

    public NoteModel Model { get; }

    public bool IsEditing => _editing;

    public event EventHandler? Changed;
    public event EventHandler? Activated;
    public event EventHandler? EditStarted;
    public event EventHandler? EditFinished;
    public event EventHandler? DragMoved;
    public event EventHandler? DragFinished;
    public event EventHandler? DeleteRequested;

    public void Activate()
    {
        Panel.SetZIndex(this, ++_zTop);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyColors()
    {
        var fill = ColorUtil.Parse(Model.FillColor);
        var font = ColorUtil.Parse(Model.FontColor);
        var fillBrush = new SolidColorBrush(fill);
        var fontBrush = new SolidColorBrush(font);

        NoteBody.Background = fillBrush;
        Editor.Foreground = fontBrush;
        Editor.CaretBrush = fontBrush;
        Editor.SelectionBrush = new SolidColorBrush(Color.FromArgb(70, 70, 110, 170));
        EmptyPlaceholder.Foreground = fontBrush;
        ModeBadge.Foreground = fontBrush;
        SizeBadge.Foreground = fontBrush;
        SelectionRing.BorderBrush = new SolidColorBrush(Color.FromArgb(180, font.R, font.G, font.B));
        HoverRing.BorderBrush = new SolidColorBrush(Color.FromArgb(55, font.R, font.G, font.B));
    }

    public void BeginEdit()
    {
        if (_editing) return;
        _editing = true;
        Activate();

        Editor.Text = Model.Text ?? string.Empty;
        Editor.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyPlaceholder.Visibility = Visibility.Collapsed;
        HoverRing.Visibility = Visibility.Collapsed;
        SelectionRing.Visibility = Visibility.Visible;
        ResizeCorner.Opacity = 0;

        EditStarted?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!_editing) return;
            Editor.Focus();
            Keyboard.Focus(Editor);
            Editor.CaretIndex = Editor.Text.Length;
        }));
    }

    public void EndEdit()
    {
        if (!_editing) return;
        _editing = false;

        Model.Text = Editor.Text ?? string.Empty;
        Editor.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;
        SelectionRing.Visibility = Visibility.Collapsed;
        RenderContent();
        Changed?.Invoke(this, EventArgs.Empty);
        EditFinished?.Invoke(this, EventArgs.Empty);
    }

    public void SetFillColor(string hex)
    {
        Model.FillColor = hex;
        ApplyColors();
        RenderContent();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFontColor(string hex)
    {
        Model.FontColor = hex;
        ApplyColors();
        RenderContent();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMarkdown(bool enabled)
    {
        if (Model.Markdown == enabled) return;
        Model.Markdown = enabled;
        if (!_editing) RenderContent();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RenderContent()
    {
        var font = ColorUtil.Parse(Model.FontColor);
        var fill = ColorUtil.Parse(Model.FillColor);

        if (Model.Markdown)
        {
            RenderedContent.Content = MarkdownRenderer.Build(Model.Text ?? string.Empty, font, fill);
        }
        else
        {
            RenderedContent.Content = new TextBlock
            {
                Text = Model.Text ?? string.Empty,
                Foreground = new SolidColorBrush(font),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12.5,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var empty = string.IsNullOrWhiteSpace(Model.Text);
        EmptyPlaceholder.Visibility = empty && !_editing ? Visibility.Visible : Visibility.Collapsed;
        ModeBadge.Text = Model.Markdown ? "MD" : "纯文本";
        UpdateSizeBadge();
    }

    private void UpdateSizeBadge()
    {
        SizeBadge.Text = $"{Width:0}×{Height:0}";
    }

    private void BuildContextMenu()
    {
        _menu = new ContextMenu();
        if (Application.Current?.TryFindResource("CoContextMenuStyle") is Style menuStyle)
            _menu.Style = menuStyle;

        var fillMenu = new MenuItem { Header = "便签颜色" };
        foreach (var hex in NoteFills)
            fillMenu.Items.Add(CreateSwatchMenuItem(hex, () => SetFillColor(hex)));
        _menu.Items.Add(fillMenu);

        var fontMenu = new MenuItem { Header = "字体颜色" };
        foreach (var hex in InkColors)
            fontMenu.Items.Add(CreateSwatchMenuItem(hex, () => SetFontColor(hex)));
        _menu.Items.Add(fontMenu);

        _menu.Items.Add(new Separator());

        _plainItem = new MenuItem { Header = "纯文本模式", IsCheckable = true, StaysOpenOnClick = false };
        _plainItem.Click += (_, _) => SetMarkdown(false);
        _menu.Items.Add(_plainItem);

        _markdownItem = new MenuItem { Header = "Markdown 模式", IsCheckable = true, StaysOpenOnClick = false };
        _markdownItem.Click += (_, _) => SetMarkdown(true);
        _menu.Items.Add(_markdownItem);

        _menu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = "删除便签",
            Foreground = new SolidColorBrush(ColorUtil.Parse("#E0524A")),
            FontWeight = FontWeights.SemiBold
        };
        deleteItem.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(deleteItem);

        RefreshMenuState();
    }

    private static MenuItem CreateSwatchMenuItem(string hex, Action action)
    {
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(ColorUtil.Parse(hex)),
            BorderBrush = new SolidColorBrush(ColorUtil.Parse("#33000000")),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = hex,
            Margin = new Thickness(7, 0, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(label);

        var item = new MenuItem { Header = panel, ToolTip = hex };
        item.Click += (_, _) => action();
        return item;
    }

    private void RefreshMenuState()
    {
        if (_plainItem != null) _plainItem.IsChecked = !Model.Markdown;
        if (_markdownItem != null) _markdownItem.IsChecked = Model.Markdown;
    }

    private Canvas? HostCanvas => Parent as Canvas;

    private void Body_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) != null) return;
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            BeginEdit();
            return;
        }
        if (_editing) return;

        var canvas = HostCanvas;
        if (canvas == null) return;

        e.Handled = true;
        Activate();
        _dragging = true;
        _moved = false;
        _dragOrigin = e.GetPosition(canvas);
        _dragStartX = Canvas.GetLeft(this);
        _dragStartY = Canvas.GetTop(this);
        if (double.IsNaN(_dragStartX)) _dragStartX = Model.X;
        if (double.IsNaN(_dragStartY)) _dragStartY = Model.Y;
        NoteBody.CaptureMouse();
    }

    private void Body_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var canvas = HostCanvas;
        if (canvas == null) return;

        var point = e.GetPosition(canvas);
        var dx = point.X - _dragOrigin.X;
        var dy = point.Y - _dragOrigin.Y;
        if (!_moved && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
        _moved = true;

        var maxX = Math.Max(0, canvas.ActualWidth - Width);
        var maxY = Math.Max(0, canvas.ActualHeight - Height);
        var x = Math.Clamp(_dragStartX + dx, 0, maxX);
        var y = Math.Clamp(_dragStartY + dy, 0, maxY);
        Canvas.SetLeft(this, x);
        Canvas.SetTop(this, y);
        Model.X = x;
        Model.Y = y;
        DragMoved?.Invoke(this, EventArgs.Empty);
    }

    private void Body_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        NoteBody.ReleaseMouseCapture();

        if (_moved)
        {
            DragFinished?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            BeginEdit();
        }
    }

    private void Body_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) != null) return;
        if (_editing) EndEdit();
        Activate();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_editing) return;
        // Keep the badge in sync while typing so the mode stays visible.
        ModeBadge.Text = Model.Markdown ? "MD" : "纯文本";
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape ||
            (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
        {
            e.Handled = true;
            EndEdit();
        }
    }

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_editing) return;
        if (Editor.IsKeyboardFocusWithin) return;
        EndEdit();
    }

    private void ResizeCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var canvas = HostCanvas;
        if (canvas == null || _editing) return;
        e.Handled = true;
        Activate();
        _resizing = true;
        _resizeOrigin = e.GetPosition(canvas);
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        ResizeCorner.CaptureMouse();
    }

    private void ResizeCorner_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing || e.LeftButton != MouseButtonState.Pressed) return;
        var canvas = HostCanvas;
        if (canvas == null) return;

        var point = e.GetPosition(canvas);
        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);
        if (double.IsNaN(left)) left = Model.X;
        if (double.IsNaN(top)) top = Model.Y;

        var maxWidth = Math.Max(140, canvas.ActualWidth - left);
        var maxHeight = Math.Max(110, canvas.ActualHeight - top);
        Width = Math.Clamp(_resizeStartWidth + (point.X - _resizeOrigin.X), 140, maxWidth);
        Height = Math.Clamp(_resizeStartHeight + (point.Y - _resizeOrigin.Y), 110, maxHeight);
        Model.Width = Width;
        Model.Height = Height;
        UpdateSizeBadge();
        e.Handled = true;
    }

    private void ResizeCorner_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ResizeCorner.ReleaseMouseCapture();
        Model.Width = Width;
        Model.Height = Height;
        Changed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}
