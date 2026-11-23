using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoTab.Helpers;
using CoTab.Models;
using CoTab.Services;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CoTab;

public partial class MainWindow : Window
{
    private const double CollapsedTabThicknessDip = 14;
    private const double CollapsedTabLengthDip = 88;
    private const double CollapsedEdgeMarginDip = 24;
    private const int CollapseSlideMilliseconds = 240;
    private const int CollapseTabMilliseconds = 170;
    private const int ExpandSlideMilliseconds = 280;
    private const double MinimumWindowWidth = 360;
    private const double MinimumWindowHeight = 420;

    private readonly bool _animationsEnabled = !string.Equals(
        Environment.GetEnvironmentVariable("COTAB_DISABLE_ANIMATIONS"), "1", StringComparison.OrdinalIgnoreCase);
    private bool _animating;

    private readonly AppState _state;
    private readonly bool _previewMode;
    private readonly List<NoteControl> _notes = new();
    private readonly Dictionary<string, ColorRow> _inkRows = new();

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.4) };

    private ColorRow? _panelColorRow;
    private ColorRow? _noteFillRow;
    private ColorRow? _noteFontRow;

    private bool _uiUpdating;
    private bool _settingsOpen;
    private bool _isCollapsed;
    private bool _closing;
    private bool _suppressAutoHide;
    private bool _resizingWindow;
    private bool _noteMenuOpen;
    private string _resizeEdge = string.Empty;
    private Point _resizeStartScreen;
    private Rect _resizeStartBounds;
    private double _expandedLeft;
    private double _expandedTop;
    private double _expandedWidth;
    private double _expandedHeight;
    private Rect _expandedScreenPixels;
    private DockEdge _dockEdge;
    private NoteControl? _pendingDelete;
    private Action? _undoAction;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Drawing.Icon? _trayDrawingIcon;

    public MainWindow() : this(new AppState())
    {
    }

    public MainWindow(AppState state, bool previewMode = false)
    {
        _state = state ?? new AppState();
        _previewMode = previewMode;
        _uiUpdating = true;

        InitializeComponent();

        Width = Math.Max(MinimumWindowWidth, _state.Window.Width);
        Height = Math.Max(MinimumWindowHeight, _state.Window.Height);

        ShadowLayer.Visibility = Visibility.Collapsed;

        BuildSettingsRows();
        RestoreNotes();
        ApplyTheme();
        ApplyBackground();
        ApplyTrashVisibility();
        RefreshSettingsUi();
        UpdateEmptyHint();
        Topmost = _state.Settings.AlwaysOnTop;
        _uiUpdating = false;
        RefreshChromeState();

        if (!_previewMode)
        {
            PlaceWindow();
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SaveNow();
            };
            _autoHideTimer.Tick += AutoHideTimer_Tick;
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                FadeToast();
            };
            PreviewMouseMove += Window_PreviewMouseMove;
            PreviewMouseLeftButtonUp += Window_PreviewMouseLeftButtonUp;
            KeyDown += Window_KeyDown;
            LocationChanged += (_, _) => UpdateEdgeIndicator();
            SizeChanged += (_, _) => UpdateEdgeIndicator();
        }
    }

    // ---------------------------------------------------------------- lifecycle

    private void PlaceWindow()
    {
        var saved = _state.Window;
        Width = Math.Max(MinimumWindowWidth, saved.Width);
        Height = Math.Max(MinimumWindowHeight, saved.Height);

        if (saved.HasPosition && ScreenUtil.IsPositionVisible(saved.Left, saved.Top, Width, Height))
        {
            Left = saved.Left;
            Top = saved.Top;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 20;
            Top = work.Top + Math.Max(20, (work.Height - Height) / 2);
            saved.Left = Left;
            saved.Top = Top;
            saved.HasPosition = true;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_previewMode) return;

        ClampNotesToCanvas();
        _state.Window.Width = Width;
        _state.Window.Height = Height;

        if (_state.Settings.StartWithWindows)
        {
            if (!StartupService.IsEnabled())
                StartupService.SetEnabled(true, out _);
            else
                StartupService.RefreshIfEnabled();
        }

        _dockEdge = ScreenUtil.DetectDock(this);
        UpdateEdgeIndicator();
        if (_dockEdge != DockEdge.None)
            ScheduleAutoHide(1.8);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_previewMode) return;
        // EndEdit raises EditFinished, and that handler normally restarts the
        // auto-hide timer. Once the window is closing its visual tree is no
        // longer connected to a PresentationSource, so a late CollapsePanel
        // would throw "This Visual is not connected to a PresentationSource".
        _closing = true;
        _saveTimer.Stop();
        _autoHideTimer.Stop();
        _toastTimer.Stop();
        StopAnimations();
        foreach (var note in _notes.Where(n => n.IsEditing).ToList())
            note.EndEdit();
        SaveNow();
        DisposeTrayIcon();
    }

    private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        if (_previewMode || _animating) return;
        if (_isCollapsed)
        {
            SizeAndPositionCollapsedTab(_dockEdge);
        }
        else
        {
            ClampNotesToCanvas();
        }
        UpdateEdgeIndicator();
        QueueSave();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_previewMode || _settingsOpen || _noteMenuOpen || _isCollapsed) return;
        if (Application.Current?.Windows.OfType<Window>().Any(w => !ReferenceEquals(w, this) && w.IsVisible) == true)
            return;
        if (_state.Settings.AutoHide)
            ScheduleAutoHide(0.9);
    }

    // -------------------------------------------------------------- theme/backdrop

    private void ApplyTheme()
    {
        var settings = _state.Settings;
        var panel = ColorUtil.Parse(settings.PanelColor, Colors.White);
        var alpha = Math.Clamp(settings.PanelOpacity, 0.18, 1);

        PanelBase.Background = new SolidColorBrush(ColorUtil.WithAlpha(panel, alpha));
        PanelBase.Effect = new DropShadowEffect
        {
            BlurRadius = 20,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = 0.5,
            Color = Colors.Black
        };

        var darkPanel = ColorUtil.Luminance(panel) < 0.46;
        PanelEdge.BorderBrush = new SolidColorBrush(darkPanel
            ? Color.FromArgb(42, 255, 255, 255)
            : Color.FromArgb(28, 0, 0, 0));

        var settingsInk = ColorUtil.Parse(settings.SettingsColor, ColorUtil.Parse("#3B3C42", Colors.Black));
        var closeInk = ColorUtil.Parse(settings.CloseColor, ColorUtil.Parse("#3B3C42", Colors.Black));
        var addInk = ColorUtil.Parse(settings.AddColor, ColorUtil.Parse("#3B3C42", Colors.Black));
        var muted = ColorUtil.DeriveMuted(settingsInk, darkPanel ? 0.28 : 0.42);

        SettingsGlyph.Fill = new SolidColorBrush(settingsInk);
        CloseGlyph.Stroke = new SolidColorBrush(closeInk);
        PinGlyph.Fill = new SolidColorBrush(closeInk);
        AutoHideGlyph.Fill = new SolidColorBrush(closeInk);
        AddNotePlus.Foreground = new SolidColorBrush(addInk);
        AddNoteText.Foreground = new SolidColorBrush(addInk);

        EmptyHintGlyph.Stroke = new SolidColorBrush(settingsInk);
        EmptyHintTitle.Foreground = new SolidColorBrush(settingsInk);
        EmptyHintSub.Foreground = new SolidColorBrush(muted);
        TrashGlyph.Stroke = new SolidColorBrush(muted);
        TrashLabel.Foreground = new SolidColorBrush(muted);
        TrashZone.BorderBrush = Brushes.Transparent;

        CollapsedTab.Background = new SolidColorBrush(ColorUtil.WithAlpha(panel, Math.Clamp(settings.PanelOpacity, 0.72, 0.97)));
        CollapsedTab.BorderBrush = new SolidColorBrush(darkPanel
            ? Color.FromArgb(54, 255, 255, 255)
            : Color.FromArgb(36, 0, 0, 0));
        foreach (var dot in CollapsedDots.Children.OfType<System.Windows.Shapes.Ellipse>())
            dot.Fill = new SolidColorBrush(ColorUtil.WithAlpha(settingsInk, 0.62));

        var app = Application.Current;
        if (app != null)
        {
            app.Resources["IconBrush"] = new SolidColorBrush(settingsInk);
            app.Resources["IconHoverBrush"] = new SolidColorBrush(ColorUtil.Blend(settingsInk, darkPanel ? Colors.White : Colors.Black, 0.28));
            app.Resources["IconPressedBrush"] = new SolidColorBrush(ColorUtil.Blend(settingsInk, darkPanel ? Colors.White : Colors.Black, 0.45));
            app.Resources["MutedBrush"] = new SolidColorBrush(muted);
            app.Resources["PanelBorderBrush"] = new SolidColorBrush(darkPanel
                ? Color.FromArgb(42, 255, 255, 255)
                : Color.FromArgb(28, 0, 0, 0));
        }

        ApplyInkHalo();
        ApplyChromePills();
        RefreshChromeState();
    }

    /// <summary>
    /// Over a photo the ink colors can land on a similar-tone area. A subtle
    /// panel-toned glow keeps the three chrome icons legible without washing out the image.
    /// </summary>
    private void ApplyInkHalo()
    {
        DropShadowEffect? halo = null;
        if (_state.Settings.BackgroundStyle == PanelBackgroundStyle.Image)
        {
            var ink = ColorUtil.Parse(_state.Settings.SettingsColor, ColorUtil.Parse("#3B3C42", Colors.Black));
            halo = new DropShadowEffect
            {
                Color = ColorUtil.Luminance(ink) < 0.5 ? Colors.White : Colors.Black,
                BlurRadius = 5,
                ShadowDepth = 0,
                Opacity = 0.9
            };
            halo.Freeze();
        }

        SettingsGlyph.Effect = halo;
        CloseGlyph.Effect = halo;
        PinGlyph.Effect = halo;
        AutoHideGlyph.Effect = halo;
        AddNotePlus.Effect = halo;
        AddNoteText.Effect = halo;
        EmptyHintGlyph.Effect = halo;
        EmptyHintTitle.Effect = halo;
        EmptyHintSub.Effect = halo;
        TrashGlyph.Effect = halo;
        TrashLabel.Effect = halo;
    }

    private void ApplyBackground()
    {
        var settings = _state.Settings;
        PatternLayer.Visibility = Visibility.Collapsed;
        ImageLayer.Visibility = Visibility.Collapsed;
        ImageLayer.Background = null;

        var panel = ColorUtil.Parse(settings.PanelColor, Colors.White);
        var darkPanel = ColorUtil.Luminance(panel) < 0.46;
        var ink = darkPanel ? Colors.White : Colors.Black;

        switch (settings.BackgroundStyle)
        {
            case PanelBackgroundStyle.Dots:
                PatternLayer.Background = CreatePatternBrush(true, ink);
                PatternLayer.Visibility = Visibility.Visible;
                break;
            case PanelBackgroundStyle.Cross:
                PatternLayer.Background = CreatePatternBrush(false, ink);
                PatternLayer.Visibility = Visibility.Visible;
                break;
            case PanelBackgroundStyle.Image:
                var path = settings.BackgroundImagePath;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var image = TryLoadImage(path);
                    if (image != null)
                    {
                        var brush = new ImageBrush(image)
                        {
                            Stretch = Stretch.UniformToFill,
                            AlignmentX = AlignmentX.Center,
                            AlignmentY = AlignmentY.Center
                        };
                        brush.Freeze();
                        ImageLayer.Background = brush;
                        ImageLayer.Opacity = Math.Clamp(settings.BackgroundImageOpacity, 0.15, 1);
                        ImageLayer.Visibility = Visibility.Visible;
                    }
                }
                break;
        }

        ApplyInkHalo();
        ApplyChromePills();
        RefreshChromeState();
    }

    private static DrawingBrush CreatePatternBrush(bool dots, Color ink)
    {
        // Dots are small round points; the cross style is a sparse field of
        // plus marks (not full graph-paper lines) so it stays quiet under notes.
        var tile = dots ? 18.0 : 22.0;
        var color = Color.FromArgb((byte)(dots ? 58 : 62), ink.R, ink.G, ink.B);
        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };

        var drawing = new DrawingGroup();
        if (dots)
        {
            drawing.Children.Add(new GeometryDrawing(
                new SolidColorBrush(color),
                null,
                new EllipseGeometry(new Point(3, 3), 1.15, 1.15)));
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(color), 1);
            pen.Freeze();
            var center = tile / 2;
            const double arm = 2.5;
            drawing.Children.Add(new GeometryDrawing(null, pen,
                new LineGeometry(new Point(center - arm, center), new Point(center + arm, center))));
            drawing.Children.Add(new GeometryDrawing(null, pen,
                new LineGeometry(new Point(center, center - arm), new Point(center, center + arm))));
        }

        drawing.Freeze();
        brush.Drawing = drawing;
        brush.Freeze();
        return brush;
    }

    private static BitmapSource? TryLoadImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];
            if (frame.CanFreeze) frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            StateStore.Log("加载背景图片失败: " + ex);
            return null;
        }
    }

    // ---------------------------------------------------------------- note board

    private void RestoreNotes()
    {
        foreach (var model in _state.Notes.ToList())
        {
            var note = CreateNote(model);
            NotesCanvas.Children.Add(note);
            _notes.Add(note);
        }
    }

    private NoteControl CreateNote(NoteModel model)
    {
        var note = new NoteControl(model);
        Canvas.SetLeft(note, model.X);
        Canvas.SetTop(note, model.Y);

        note.Changed += (_, _) => QueueSave();
        note.EditStarted += (_, _) =>
        {
            _autoHideTimer.Stop();
            _suppressAutoHide = true;
        };
        note.EditFinished += (_, _) =>
        {
            _suppressAutoHide = false;
            ScheduleAutoHide(1.2);
        };
        note.DragMoved += (_, _) =>
        {
            _autoHideTimer.Stop();
            UpdateTrashHighlight(note);
        };
        note.DragFinished += (_, _) => FinishNoteDrag(note);
        note.DeleteRequested += (_, _) => RequestDelete(note);
        note.Activated += (_, _) => { };

        note.ContextMenuOpening += (_, _) =>
        {
            _noteMenuOpen = true;
            _autoHideTimer.Stop();
        };
        if (note.ContextMenu != null)
            note.ContextMenu.Closed += (_, _) =>
            {
                _noteMenuOpen = false;
                ResetTrashVisual();
                ScheduleAutoHide(1.2);
            };

        return note;
    }

    private void AddNote()
    {
        if (_isCollapsed) ExpandPanel();
        if (_settingsOpen) HideSettings();

        var canvasWidth = NotesCanvas.ActualWidth > 0 ? NotesCanvas.ActualWidth : Math.Max(160, Width - 56);
        var canvasHeight = NotesCanvas.ActualHeight > 0 ? NotesCanvas.ActualHeight : Math.Max(120, Height - 120);
        var noteWidth = Math.Clamp(184, 140, Math.Max(140, canvasWidth - 8));
        var noteHeight = Math.Clamp(184, 110, Math.Max(110, canvasHeight - 8));

        var model = new NoteModel
        {
            Width = noteWidth,
            Height = noteHeight,
            FillColor = _state.Settings.NoteFillColor,
            FontColor = _state.Settings.NoteFontColor,
            Markdown = true,
            X = Math.Max(0, (canvasWidth - noteWidth) / 2),
            Y = Math.Max(0, (canvasHeight - noteHeight) / 2)
        };

        var note = CreateNote(model);
        Canvas.SetLeft(note, model.X);
        Canvas.SetTop(note, model.Y);
        NotesCanvas.Children.Add(note);
        _notes.Add(note);
        _state.Notes.Add(model);

        UpdateEmptyHint();
        QueueSave();
        note.BeginEdit();
    }

    private void DeleteNote(NoteControl note, bool offerUndo)
    {
        if (!_notes.Remove(note)) return;
        if (note.IsEditing) note.EndEdit();
        NotesCanvas.Children.Remove(note);

        var model = note.Model;
        _state.Notes.Remove(model);

        if (offerUndo)
        {
            _undoAction = () => RestoreDeletedNote(model);
            ShowToast("便签已删除 · 点击此处撤销", _undoAction);
        }
        else
        {
            ShowToast("便签已删除");
        }

        UpdateEmptyHint();
        QueueSave();
    }

    private void RestoreDeletedNote(NoteModel model)
    {
        if (_state.Notes.Any(n => string.Equals(n.Id, model.Id, StringComparison.OrdinalIgnoreCase))) return;
        EnsureNoteInCanvas(model);
        _state.Notes.Add(model);
        var note = CreateNote(model);
        NotesCanvas.Children.Add(note);
        _notes.Add(note);
        UpdateEmptyHint();
        QueueSave();
    }

    private void EnsureNoteInCanvas(NoteModel model)
    {
        var width = NotesCanvas.ActualWidth > 0 ? NotesCanvas.ActualWidth : Math.Max(160, Width - 56);
        var height = NotesCanvas.ActualHeight > 0 ? NotesCanvas.ActualHeight : Math.Max(120, Height - 120);
        model.Width = Math.Clamp(model.Width, 140, Math.Max(140, width));
        model.Height = Math.Clamp(model.Height, 110, Math.Max(110, height));
        model.X = Math.Clamp(model.X, 0, Math.Max(0, width - model.Width));
        model.Y = Math.Clamp(model.Y, 0, Math.Max(0, height - model.Height));
    }

    private void ClampNotesToCanvas()
    {
        if (NotesCanvas.ActualWidth <= 0 || NotesCanvas.ActualHeight <= 0) return;
        foreach (var note in _notes)
        {
            EnsureNoteInCanvas(note.Model);
            note.Width = note.Model.Width;
            note.Height = note.Model.Height;
            Canvas.SetLeft(note, note.Model.X);
            Canvas.SetTop(note, note.Model.Y);
        }
    }

    private void UpdateEmptyHint()
    {
        EmptyHint.Visibility = _notes.Count == 0 && !_settingsOpen && !_isCollapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- trash

    private void ApplyTrashVisibility()
    {
        TrashZone.Visibility = _state.Settings.TrashEnabled ? Visibility.Visible : Visibility.Collapsed;
        ConfirmDeleteSwitch.IsEnabled = _state.Settings.TrashEnabled;
        if (!_state.Settings.TrashEnabled) ResetTrashVisual();
    }

    private bool IsOverTrash(NoteControl note)
    {
        if (!_state.Settings.TrashEnabled || TrashZone.Visibility != Visibility.Visible) return false;
        try
        {
            // Use the pointer, not the note's clamped center: a tall note can
            // never be centered over the bottom-left trash zone while its top
            // left corner is still clamped inside the canvas.
            var p = Mouse.GetPosition(TrashZone);
            return p.X >= 0 && p.Y >= 0 && p.X <= TrashZone.ActualWidth && p.Y <= TrashZone.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateTrashHighlight(NoteControl note)
    {
        if (IsOverTrash(note))
        {
            TrashZone.Background = new SolidColorBrush(Color.FromRgb(232, 84, 74));
            TrashZone.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 138, 128));
            TrashLabel.Text = "松开删除";
            TrashLabel.Foreground = Brushes.White;
            TrashGlyph.Stroke = Brushes.White;
        }
        else
        {
            ResetTrashVisual();
        }
    }

    private void ResetTrashVisual()
    {
        var app = Application.Current;
        TrashZone.Background = ChromePillBrush()
            ?? app?.TryFindResource("TrashIdleBrush") as Brush
            ?? new SolidColorBrush(Color.FromArgb(0x16, 0, 0, 0));
        TrashZone.BorderBrush = Brushes.Transparent;
        TrashLabel.Text = "拖到此处删除";
        TrashLabel.Foreground = app?.TryFindResource("MutedBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x6A, 0x6C, 0x74));
        TrashGlyph.Stroke = app?.TryFindResource("MutedBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x6A, 0x6C, 0x74));
    }

    /// <summary>A frosted pill behind chrome controls when a photo sits behind them.</summary>
    private Brush? ChromePillBrush()
    {
        if (_state.Settings.BackgroundStyle != PanelBackgroundStyle.Image) return null;
        var panel = ColorUtil.Parse(_state.Settings.PanelColor, Colors.White);
        return new SolidColorBrush(Color.FromArgb(154, panel.R, panel.G, panel.B));
    }

    private void ApplyChromePills()
    {
        var pill = ChromePillBrush();
        if (pill != null)
        {
            SettingsButton.Background = pill;
            CloseButton.Background = pill;
            AddNoteButton.Background = pill;
            TrashZone.Background = pill;
            return;
        }

        SettingsButton.ClearValue(Control.BackgroundProperty);
        CloseButton.ClearValue(Control.BackgroundProperty);
        AddNoteButton.ClearValue(Control.BackgroundProperty);
        PinButton.ClearValue(Control.BackgroundProperty);
        AutoHideButton.ClearValue(Control.BackgroundProperty);
        ResetTrashVisual();
    }

    /// <summary>
    /// Pin and auto-collapse buttons are two-state: dark like the close X when
    /// on, translucent when off. Their background is never used for state.
    /// </summary>
    private void RefreshChromeState()
    {
        if (_uiUpdating) return;
        var settings = _state.Settings;
        var closeInk = ColorUtil.Parse(settings.CloseColor, ColorUtil.Parse("#3B3C42", Colors.Black));
        var offInk = ColorUtil.WithAlpha(closeInk, 0.40);
        var imagePill = ChromePillBrush();

        PinButton.Background = imagePill ?? Brushes.Transparent;
        AutoHideButton.Background = imagePill ?? Brushes.Transparent;
        PinGlyph.Fill = new SolidColorBrush(settings.AlwaysOnTop ? closeInk : offInk);
        AutoHideGlyph.Fill = new SolidColorBrush(settings.AutoHide ? closeInk : offInk);
        UpdateEdgeIndicator();
    }

    /// <summary>Rotate the triangle toward the edge the panel will retreat to.</summary>
    private void UpdateEdgeIndicator()
    {
        if (_closing) return;
        var edge = _isCollapsed
            ? _dockEdge
            : (IsLoaded ? ScreenUtil.NearestEdge(this) : DockEdge.Right);
        var angle = edge switch
        {
            DockEdge.Bottom => 0.0,
            DockEdge.Left => 90.0,
            DockEdge.Top => 180.0,
            DockEdge.Right => 270.0,
            _ => 0.0
        };
        AutoHideGlyphRotation.Angle = angle;

        var direction = edge switch
        {
            DockEdge.Bottom => "下",
            DockEdge.Left => "左",
            DockEdge.Top => "上",
            DockEdge.Right => "右",
            _ => "最近"
        };
        AutoHideButton.ToolTip = _state.Settings.AutoHide
            ? $"自动收起：开（将收起到屏幕{direction}侧）"
            : "自动收起：关";
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = _state.Settings;
        settings.AlwaysOnTop = !settings.AlwaysOnTop;
        Topmost = settings.AlwaysOnTop;
        _uiUpdating = true;
        TopmostSwitch.IsChecked = settings.AlwaysOnTop;
        _uiUpdating = false;
        if (Topmost) Activate();
        RefreshChromeState();
        QueueSave();
        ShowToast(settings.AlwaysOnTop ? "已保持置顶" : "已取消置顶");
    }

    private void AutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = _state.Settings;
        settings.AutoHide = !settings.AutoHide;
        _uiUpdating = true;
        AutoHideSwitch.IsChecked = settings.AutoHide;
        _uiUpdating = false;

        if (settings.AutoHide)
        {
            ScheduleAutoHide(1.4);
        }
        else
        {
            _autoHideTimer.Stop();
            if (_isCollapsed) ExpandPanel();
        }

        RefreshChromeState();
        QueueSave();
        ShowToast(settings.AutoHide ? "已开启自动收起" : "已关闭自动收起");
    }

    private void FinishNoteDrag(NoteControl note)
    {
        var over = IsOverTrash(note);
        ResetTrashVisual();
        if (!over) return;
        RequestDelete(note);
    }

    private void RequestDelete(NoteControl note)
    {
        if (_state.Settings.ConfirmTrashDelete)
        {
            _pendingDelete = note;
            ConfirmMessage.Text = "删除后可以点击提示条撤销。";
            ConfirmOverlay.Visibility = Visibility.Visible;
            _autoHideTimer.Stop();
        }
        else
        {
            DeleteNote(note, true);
        }
    }

    private void HideConfirm()
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        _pendingDelete = null;
        ScheduleAutoHide(1.2);
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e) => HideConfirm();

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        var note = _pendingDelete;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        _pendingDelete = null;
        if (note != null) DeleteNote(note, true);
        ScheduleAutoHide(1.2);
    }

    // ---------------------------------------------------------------- toast

    private void ShowToast(string message, Action? action = null)
    {
        _toastTimer.Stop();
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastText.Text = message;
        ToastHost.Opacity = 1;
        ToastHost.Visibility = Visibility.Visible;
        _undoAction = action;
        _toastTimer.Start();
    }

    private void FadeToast()
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        animation.Completed += (_, _) =>
        {
            ToastHost.Visibility = Visibility.Collapsed;
            ToastHost.BeginAnimation(OpacityProperty, null);
            ToastHost.Opacity = 0;
            _undoAction = null;
        };
        ToastHost.BeginAnimation(OpacityProperty, animation);
    }

    private void ToastHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var action = _undoAction;
        _undoAction = null;
        _toastTimer.Stop();
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastHost.Opacity = 0;
        ToastHost.Visibility = Visibility.Collapsed;
        action?.Invoke();
    }

    // ---------------------------------------------------------------- settings UI

    private void BuildSettingsRows()
    {
        var settings = _state.Settings;

        _panelColorRow = new ColorRow("背景颜色", ColorUtil.Parse(settings.PanelColor, Colors.White));
        _panelColorRow.ColorChanged += (_, _) =>
        {
            if (_uiUpdating || _panelColorRow == null) return;
            settings.PanelColor = ColorUtil.ToHex(_panelColorRow.Color);
            ApplyTheme();
            ApplyBackground();
            QueueSave();
        };
        PanelColorRowHost.Children.Add(_panelColorRow);

        _inkRows["settings"] = AddInkRow("设置图标", settings.SettingsColor, value =>
        {
            settings.SettingsColor = value;
            ApplyTheme();
        });
        _inkRows["close"] = AddInkRow("关闭按钮", settings.CloseColor, value =>
        {
            settings.CloseColor = value;
            ApplyTheme();
        });
        _inkRows["add"] = AddInkRow("新增便签", settings.AddColor, value =>
        {
            settings.AddColor = value;
            ApplyTheme();
        });

        _noteFillRow = new ColorRow("默认便签颜色", ColorUtil.Parse(settings.NoteFillColor, ColorUtil.Parse("#FFF1B8", Colors.Black)));
        _noteFillRow.ColorChanged += (_, _) =>
        {
            if (_uiUpdating || _noteFillRow == null) return;
            settings.NoteFillColor = ColorUtil.ToHex(_noteFillRow.Color);
            QueueSave();
        };
        NoteColorRowsHost.Children.Add(_noteFillRow);

        _noteFontRow = new ColorRow("默认字体颜色", ColorUtil.Parse(settings.NoteFontColor, ColorUtil.Parse("#3D382B", Colors.Black)));
        _noteFontRow.ColorChanged += (_, _) =>
        {
            if (_uiUpdating || _noteFontRow == null) return;
            settings.NoteFontColor = ColorUtil.ToHex(_noteFontRow.Color);
            QueueSave();
        };
        NoteColorRowsHost.Children.Add(_noteFontRow);
    }

    private ColorRow AddInkRow(string label, string hex, Action<string> apply)
    {
        var row = new ColorRow(label, ColorUtil.Parse(hex, ColorUtil.Parse("#3B3C42", Colors.Black)));
        row.ColorChanged += (_, _) =>
        {
            if (_uiUpdating) return;
            apply(ColorUtil.ToHex(row.Color));
            QueueSave();
        };
        InkColorRowsHost.Children.Add(row);
        return row;
    }

    private void RefreshSettingsUi()
    {
        _uiUpdating = true;
        try
        {
            var settings = _state.Settings;

            PanelOpacitySlider.Value = Math.Clamp(settings.PanelOpacity * 100, PanelOpacitySlider.Minimum, PanelOpacitySlider.Maximum);
            PanelOpacityValue.Text = $"{PanelOpacitySlider.Value:0}%";
            ImageOpacitySlider.Value = Math.Clamp(settings.BackgroundImageOpacity * 100, ImageOpacitySlider.Minimum, ImageOpacitySlider.Maximum);
            ImageOpacityValue.Text = $"{ImageOpacitySlider.Value:0}%";

            StyleBlankRadio.IsChecked = settings.BackgroundStyle == PanelBackgroundStyle.Blank;
            StyleDotsRadio.IsChecked = settings.BackgroundStyle == PanelBackgroundStyle.Dots;
            StyleCrossRadio.IsChecked = settings.BackgroundStyle == PanelBackgroundStyle.Cross;
            StyleImageRadio.IsChecked = settings.BackgroundStyle == PanelBackgroundStyle.Image;

            _panelColorRow?.SetColor(ColorUtil.Parse(settings.PanelColor, Colors.White), false);
            _inkRows["settings"].SetColor(ColorUtil.Parse(settings.SettingsColor, ColorUtil.Parse("#3B3C42", Colors.Black)), false);
            _inkRows["close"].SetColor(ColorUtil.Parse(settings.CloseColor, ColorUtil.Parse("#3B3C42", Colors.Black)), false);
            _inkRows["add"].SetColor(ColorUtil.Parse(settings.AddColor, ColorUtil.Parse("#3B3C42", Colors.Black)), false);
            _noteFillRow?.SetColor(ColorUtil.Parse(settings.NoteFillColor, ColorUtil.Parse("#FFF1B8", Colors.Black)), false);
            _noteFontRow?.SetColor(ColorUtil.Parse(settings.NoteFontColor, ColorUtil.Parse("#3D382B", Colors.Black)), false);

            AutoHideSwitch.IsChecked = settings.AutoHide;
            TopmostSwitch.IsChecked = settings.AlwaysOnTop;
            StartupSwitch.IsChecked = settings.StartWithWindows;
            TrashSwitch.IsChecked = settings.TrashEnabled;
            ConfirmDeleteSwitch.IsChecked = settings.ConfirmTrashDelete;

            var hasImage = !string.IsNullOrWhiteSpace(settings.BackgroundImagePath) && File.Exists(settings.BackgroundImagePath);
            ImageNameText.Text = hasImage
                ? Path.GetFileName(settings.BackgroundImagePath)
                : "未选择图片";
            ImageNameText.ToolTip = hasImage ? settings.BackgroundImagePath : null;
            ExtractThemeButton.IsEnabled = hasImage;
            ClearImageButton.IsEnabled = hasImage;
            ImageOpacitySlider.IsEnabled = hasImage;
        }
        finally
        {
            _uiUpdating = false;
        }
        RefreshChromeState();
    }

    private void ShowSettings()
    {
        if (_isCollapsed) ExpandPanel();
        _settingsOpen = true;
        _autoHideTimer.Stop();
        RefreshSettingsUi();
        SettingsOverlay.Visibility = Visibility.Visible;
        UpdateEmptyHint();
        SettingsOverlay.Focus();
    }

    private void HideSettings()
    {
        _settingsOpen = false;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        UpdateEmptyHint();
        QueueSave();
        ScheduleAutoHide(1.4);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCollapsed) ExpandPanel();
        if (_settingsOpen) HideSettings();
        else ShowSettings();
    }

    private void SettingsDoneButton_Click(object sender, RoutedEventArgs e) => HideSettings();

    private void PanelOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_uiUpdating) return;
        _state.Settings.PanelOpacity = Math.Clamp(e.NewValue / 100.0, 0.18, 1);
        PanelOpacityValue.Text = $"{e.NewValue:0}%";
        ApplyTheme();
        QueueSave();
    }

    private void ImageOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_uiUpdating) return;
        _state.Settings.BackgroundImageOpacity = Math.Clamp(e.NewValue / 100.0, 0.15, 1);
        ImageOpacityValue.Text = $"{e.NewValue:0}%";
        ApplyBackground();
        QueueSave();
    }

    private void BackgroundStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (_uiUpdating || sender is not RadioButton radio || radio.Tag is not string tag) return;
        if (!Enum.TryParse<PanelBackgroundStyle>(tag, true, out var style)) return;
        _state.Settings.BackgroundStyle = style;
        ApplyBackground();
        QueueSave();
    }

    private void PickImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var destination = StateStore.ImportBackgroundImage(dialog.FileName);
        if (string.IsNullOrWhiteSpace(destination))
        {
            ShowToast("无法导入这张图片");
            return;
        }

        _state.Settings.BackgroundImagePath = destination;
        _state.Settings.BackgroundStyle = PanelBackgroundStyle.Image;
        RefreshSettingsUi();
        ApplyBackground();
        QueueSave();
        ShowToast("背景图片已更新");
    }

    private void ExtractThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _state.Settings.BackgroundImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowToast("请先选择一张背景图片");
            return;
        }

        var image = TryLoadImage(path);
        if (image == null)
        {
            ShowToast("无法读取这张图片");
            return;
        }

        var panel = ColorUtil.Parse(_state.Settings.PanelColor, Colors.White);
        var theme = ThemeColorExtractor.Extract(image, panel);
        var hex = ColorUtil.ToHex(theme);
        _state.Settings.SettingsColor = hex;
        _state.Settings.CloseColor = hex;
        _state.Settings.AddColor = hex;

        RefreshSettingsUi();
        ApplyTheme();
        QueueSave();
        ShowToast($"已提取主题色 {hex}");
    }

    private void ClearImageButton_Click(object sender, RoutedEventArgs e)
    {
        StateStore.DeleteBackgroundImage(_state.Settings.BackgroundImagePath);
        _state.Settings.BackgroundImagePath = null;
        _state.Settings.BackgroundStyle = PanelBackgroundStyle.Blank;
        RefreshSettingsUi();
        ApplyBackground();
        QueueSave();
        ShowToast("背景图片已移除");
    }

    private void AutoHideSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiUpdating) return;
        _state.Settings.AutoHide = AutoHideSwitch.IsChecked == true;
        if (_state.Settings.AutoHide) ScheduleAutoHide(1.4);
        else
        {
            _autoHideTimer.Stop();
            if (_isCollapsed) ExpandPanel();
        }
        RefreshChromeState();
        QueueSave();
    }

    private void TopmostSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiUpdating) return;
        _state.Settings.AlwaysOnTop = TopmostSwitch.IsChecked == true;
        Topmost = _state.Settings.AlwaysOnTop;
        if (Topmost) Activate();
        RefreshChromeState();
        QueueSave();
    }

    private void StartupSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiUpdating) return;
        var enabled = StartupSwitch.IsChecked == true;
        if (!StartupService.SetEnabled(enabled, out var error))
        {
            _uiUpdating = true;
            StartupSwitch.IsChecked = !enabled;
            _uiUpdating = false;
            ShowToast("无法修改开机启动：" + (error ?? "未知错误"));
            return;
        }

        _state.Settings.StartWithWindows = enabled;
        QueueSave();
        ShowToast(enabled ? "已开启开机自动启动" : "已关闭开机自动启动");
    }

    private void TrashSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiUpdating) return;
        _state.Settings.TrashEnabled = TrashSwitch.IsChecked == true;
        ApplyTrashVisibility();
        QueueSave();
    }

    private void ConfirmDeleteSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiUpdating) return;
        _state.Settings.ConfirmTrashDelete = ConfirmDeleteSwitch.IsChecked == true;
        QueueSave();
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e) => StateStore.OpenDataFolder();

    // ---------------------------------------------------------------- chrome

    private void WindowRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_animating)
        {
            e.Handled = true;
            return;
        }

        if (_isCollapsed)
        {
            e.Handled = true;
            ExpandPanel();
            return;
        }

        // Clicking bare panel space (not a note) should commit/exit the active editor.
        if (!IsInsideNote(e.OriginalSource as DependencyObject))
            EndAllEdits();

        _suppressAutoHide = true;
        _autoHideTimer.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _suppressAutoHide = false;
        }));
    }

    private static bool IsInsideNote(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is NoteControl) return true;
            source = source switch
            {
                FrameworkContentElement content => content.Parent,
                _ => VisualTreeHelper.GetParent(source)
            };
        }
        return false;
    }

    private void EndAllEdits()
    {
        var edited = false;
        foreach (var note in _notes.Where(n => n.IsEditing).ToList())
        {
            note.EndEdit();
            edited = true;
        }
        if (edited)
        {
            _suppressAutoHide = false;
            if (_state.Settings.AutoHide) ScheduleAutoHide(1.2);
        }
    }

    private void TitleDragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isCollapsed || e.LeftButton != MouseButtonState.Pressed) return;
        _autoHideTimer.Stop();
        _suppressAutoHide = true;
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse button is released before the move loop starts.
        }

        _dockEdge = ScreenUtil.DetectDock(this);
        if (_dockEdge != DockEdge.None)
            ScreenUtil.SnapToDock(this, _dockEdge);
        UpdateEdgeIndicator();

        _state.Window.Left = Left;
        _state.Window.Top = Top;
        _state.Window.HasPosition = true;
        QueueSave();

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _suppressAutoHide = false));
        if (_state.Settings.AutoHide) ScheduleAutoHide(1.3);
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AddNoteButton_Click(object sender, RoutedEventArgs e) => AddNote();

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isCollapsed || sender is not Border handle || handle.Tag is not string edge) return;
        e.Handled = true;
        _autoHideTimer.Stop();
        _suppressAutoHide = true;
        _resizingWindow = true;
        _resizeEdge = edge;
        _resizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartBounds = new Rect(Left, Top, Width, Height);
        handle.CaptureMouse();
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizingWindow || e.LeftButton != MouseButtonState.Pressed) return;
        ResizeWindow(e.GetPosition(this));
        e.Handled = true;
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizingWindow) return;
        _resizingWindow = false;
        Mouse.Capture(null);
        _resizeEdge = string.Empty;

        _state.Window.Left = Left;
        _state.Window.Top = Top;
        _state.Window.Width = Width;
        _state.Window.Height = Height;
        _state.Window.HasPosition = true;
        UpdateLayout();
        ClampNotesToCanvas();
        QueueSave();

        _dockEdge = ScreenUtil.DetectDock(this);
        if (_dockEdge != DockEdge.None) ScreenUtil.SnapToDock(this, _dockEdge);
        UpdateEdgeIndicator();
        _suppressAutoHide = false;
        if (_state.Settings.AutoHide) ScheduleAutoHide(1.3);
        e.Handled = true;
    }

    private void ResizeWindow(Point current)
    {
        var screen = PointToScreen(current);
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (screen.X - _resizeStartScreen.X) / dpi.DpiScaleX;
        var dy = (screen.Y - _resizeStartScreen.Y) / dpi.DpiScaleY;

        var left = _resizeStartBounds.Left;
        var top = _resizeStartBounds.Top;
        var right = _resizeStartBounds.Right;
        var bottom = _resizeStartBounds.Bottom;

        if (_resizeEdge.Contains("Left"))
            left = Math.Min(right - MinimumWindowWidth, left + dx);
        if (_resizeEdge.Contains("Right"))
            right = Math.Max(left + MinimumWindowWidth, right + dx);
        if (_resizeEdge.Contains("Top"))
            top = Math.Min(bottom - MinimumWindowHeight, top + dy);
        if (_resizeEdge.Contains("Bottom"))
            bottom = Math.Max(top + MinimumWindowHeight, bottom + dy);

        Left = left;
        Top = top;
        Width = right - left;
        Height = bottom - top;
    }

    // ---------------------------------------------------------------- auto collapse

    private void ScheduleAutoHide(double seconds)
    {
        if (_previewMode || _closing || _animating || !_state.Settings.AutoHide || _isCollapsed ||
            _suppressAutoHide || _settingsOpen || _noteMenuOpen || _resizingWindow)
            return;

        if (ScreenUtil.DetectDock(this) == DockEdge.None)
        {
            _autoHideTimer.Stop();
            return;
        }

        _autoHideTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.7, seconds));
        _autoHideTimer.Start();
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        if (_previewMode || _closing || _animating || !_state.Settings.AutoHide || _isCollapsed) return;
        if (IsMouseOver || _settingsOpen || _noteMenuOpen || _resizingWindow ||
            _pendingDelete != null || _notes.Any(n => n.IsEditing) ||
            Application.Current?.Windows.OfType<Window>().Any(w => !ReferenceEquals(w, this) && w.IsVisible) == true)
        {
            ScheduleAutoHide(1.2);
            return;
        }

        CollapsePanel();
    }

    private void CollapsePanel()
    {
        if (_isCollapsed || _previewMode || _closing || _animating) return;
        _dockEdge = ScreenUtil.DetectDock(this);
        if (_dockEdge == DockEdge.None) return;

        _expandedLeft = Left;
        _expandedTop = Top;
        _expandedWidth = Width;
        _expandedHeight = Height;
        _expandedScreenPixels = ScreenUtil.GetWindowPixels(this);

        _isCollapsed = true;
        _autoHideTimer.Stop();
        // The compact tab is far smaller than the expanded board; the window's
        // normal minimums must not clamp it back to a full-size rectangle.
        MinWidth = 0;
        MinHeight = 0;
        ResizeLayer.Visibility = Visibility.Collapsed;
        CollapsedTab.Visibility = Visibility.Collapsed;

        if (!_animationsEnabled)
        {
            ApplyCollapsedVisuals(animate: false);
            return;
        }

        _animating = true;
        WindowRoot.IsHitTestVisible = false;

        // Slide the whole window — panel, shadow and all — completely past the
        // docked screen edge. Only after it is fully gone does the small tab
        // grow back out of that edge.
        var (targetLeft, targetTop) = CollapseOffscreenTarget();
        AnimateWindowTo(targetLeft, targetTop, CollapseSlideMilliseconds, EasingMode.EaseIn, () =>
        {
            if (_closing) return;
            ApplyCollapsedVisuals(animate: true);
        });
    }

    private (double Left, double Top) CollapseOffscreenTarget()
    {
        var win = ScreenUtil.GetWindowPixels(this);
        var work = ScreenUtil.GetWorkAreaPixels(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = Left;
        var top = Top;

        switch (_dockEdge)
        {
            case DockEdge.Left:
                left -= Math.Max(0, win.Right - work.Left) / dpi.DpiScaleX;
                break;
            case DockEdge.Right:
                left += Math.Max(0, work.Right - win.Left) / dpi.DpiScaleX;
                break;
            case DockEdge.Top:
                top -= Math.Max(0, win.Bottom - work.Top) / dpi.DpiScaleY;
                break;
            case DockEdge.Bottom:
                top += Math.Max(0, work.Bottom - win.Top) / dpi.DpiScaleY;
                break;
        }

        return (left, top);
    }

    private void ApplyCollapsedVisuals(bool animate)
    {
        PanelContainer.Visibility = Visibility.Collapsed;
        ResizeLayer.Visibility = Visibility.Collapsed;
        ConfigureCollapsedTab(_dockEdge);
        CollapsedTab.Visibility = Visibility.Visible;
        CollapsedTab.Opacity = animate ? 0 : 1;
        CollapsedTab.RenderTransform = null;
        SizeAndPositionCollapsedTab(_dockEdge);
        UpdateLayout();

        if (!animate)
        {
            _animating = false;
            WindowRoot.IsHitTestVisible = true;
            UpdateEmptyHint();
            ShowTrayIcon();
            UpdateEdgeIndicator();
            return;
        }

        AnimateCollapsedTabIn();
    }

    private void AnimateCollapsedTabIn()
    {
        var horizontal = _dockEdge is DockEdge.Left or DockEdge.Right;
        var scale = new ScaleTransform(horizontal ? 0.02 : 1, horizontal ? 1 : 0.02);
        CollapsedTab.RenderTransformOrigin = _dockEdge switch
        {
            DockEdge.Left => new Point(0, 0.5),
            DockEdge.Right => new Point(1, 0.5),
            DockEdge.Top => new Point(0.5, 0),
            _ => new Point(0.5, 1)
        };
        CollapsedTab.RenderTransform = scale;

        var duration = TimeSpan.FromMilliseconds(CollapseTabMilliseconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var anim = new DoubleAnimation(0.02, 1, duration) { EasingFunction = ease };
        anim.Completed += (_, _) =>
        {
            if (_closing) return;
            CollapsedTab.BeginAnimation(OpacityProperty, null);
            CollapsedTab.RenderTransform = null;
            CollapsedTab.Opacity = 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _animating = false;
            WindowRoot.IsHitTestVisible = true;
            UpdateEmptyHint();
            ShowTrayIcon();
            UpdateEdgeIndicator();
        };

        if (horizontal)
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        else
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

        CollapsedTab.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>
    /// The collapsed state is a small fixed-size tab, not a sliver of the full
    /// panel: widening or heightening the board must not make the tab larger.
    /// </summary>
    private void ConfigureCollapsedTab(DockEdge edge)
    {
        CollapsedTab.ToolTip = "点击展开便签板";
        CollapsedTab.Width = double.NaN;
        CollapsedTab.Height = double.NaN;
        CollapsedTab.HorizontalAlignment = HorizontalAlignment.Stretch;
        CollapsedTab.VerticalAlignment = VerticalAlignment.Stretch;

        switch (edge)
        {
            case DockEdge.Right:
                CollapsedTab.CornerRadius = new CornerRadius(7, 0, 0, 7);
                CollapsedDots.Orientation = Orientation.Vertical;
                break;
            case DockEdge.Left:
                CollapsedTab.CornerRadius = new CornerRadius(0, 7, 7, 0);
                CollapsedDots.Orientation = Orientation.Vertical;
                break;
            case DockEdge.Top:
                CollapsedTab.CornerRadius = new CornerRadius(0, 0, 7, 7);
                CollapsedDots.Orientation = Orientation.Horizontal;
                break;
            default:
                CollapsedTab.CornerRadius = new CornerRadius(7, 7, 0, 0);
                CollapsedDots.Orientation = Orientation.Horizontal;
                break;
        }

        var verticalDots = CollapsedDots.Orientation == Orientation.Vertical;
        foreach (var dot in CollapsedDots.Children.OfType<System.Windows.Shapes.Ellipse>())
            dot.Margin = verticalDots ? new Thickness(0, 1.5, 0, 1.5) : new Thickness(1.5, 0, 1.5, 0);
    }

    private void SizeAndPositionCollapsedTab(DockEdge edge)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = ScreenUtil.GetWorkAreaPixels(this);
        var origin = PointToScreen(new Point(0, 0));
        var vertical = edge is DockEdge.Left or DockEdge.Right;

        Width = vertical ? CollapsedTabThicknessDip : CollapsedTabLengthDip;
        Height = vertical ? CollapsedTabLengthDip : CollapsedTabThicknessDip;

        var tabWidth = Width * dpi.DpiScaleX;
        var tabHeight = Height * dpi.DpiScaleY;
        var margin = CollapsedEdgeMarginDip * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);

        // Keep the tab where the panel was, not at the middle of the edge.
        // A board parked near the top-right corner therefore produces a tab
        // near that corner; it is still clamped away from the edge corners.
        var expanded = _expandedScreenPixels.Width > 0 && _expandedScreenPixels.Height > 0
            ? _expandedScreenPixels
            : new Rect(origin.X, origin.Y, tabWidth, tabHeight);

        var targetLeft = edge switch
        {
            DockEdge.Right => work.Right - tabWidth,
            DockEdge.Left => work.Left,
            _ => Math.Clamp(
                expanded.Left + expanded.Width / 2 - tabWidth / 2,
                work.Left + margin,
                Math.Max(work.Left + margin, work.Right - tabWidth - margin))
        };
        var targetTop = edge switch
        {
            DockEdge.Bottom => work.Bottom - tabHeight,
            DockEdge.Top => work.Top,
            _ => Math.Clamp(
                expanded.Top + expanded.Height / 2 - tabHeight / 2,
                work.Top + margin,
                Math.Max(work.Top + margin, work.Bottom - tabHeight - margin))
        };

        Left += (targetLeft - origin.X) / dpi.DpiScaleX;
        Top += (targetTop - origin.Y) / dpi.DpiScaleY;
        UpdateLayout();
    }

    private void ExpandPanel()
    {
        if (!_isCollapsed || _previewMode || _closing || _animating) return;
        _isCollapsed = false;
        HideTrayIcon();
        CollapsedTab.Visibility = Visibility.Collapsed;
        CollapsedTab.RenderTransform = null;
        CollapsedTab.Opacity = 1;
        // Resizing the tab window back to the full board would briefly show the
        // panel at the tab position before it can be moved off-screen. Keep the
        // root invisible until the off-screen start position is applied.
        WindowRoot.Opacity = 0;

        MinWidth = MinimumWindowWidth;
        MinHeight = MinimumWindowHeight;
        Width = Math.Max(MinimumWindowWidth, _expandedWidth);
        Height = Math.Max(MinimumWindowHeight, _expandedHeight);
        PanelContainer.Visibility = Visibility.Visible;
        ResizeLayer.Visibility = Visibility.Visible;
        UpdateLayout();

        if (!_animationsEnabled)
        {
            Left = _expandedLeft;
            Top = _expandedTop;
            FinishExpand();
            return;
        }

        _animating = true;
        WindowRoot.IsHitTestVisible = false;
        var (startLeft, startTop) = ExpandOffscreenStart();
        Left = startLeft;
        Top = startTop;
        UpdateLayout();
        WindowRoot.Opacity = 1;

        AnimateWindowTo(_expandedLeft, _expandedTop, ExpandSlideMilliseconds, EasingMode.EaseOut, FinishExpand);
    }

    private (double Left, double Top) ExpandOffscreenStart()
    {
        var work = ScreenUtil.GetWorkAreaPixels(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        var origin = PointToScreen(new Point(0, 0));
        var left = _expandedLeft;
        var top = _expandedTop;

        switch (_dockEdge)
        {
            case DockEdge.Left:
                left = Left + (work.Left - Width * dpi.DpiScaleX - origin.X) / dpi.DpiScaleX;
                break;
            case DockEdge.Right:
                left = Left + (work.Right - origin.X) / dpi.DpiScaleX;
                break;
            case DockEdge.Top:
                top = Top + (work.Top - Height * dpi.DpiScaleY - origin.Y) / dpi.DpiScaleY;
                break;
            case DockEdge.Bottom:
                top = Top + (work.Bottom - origin.Y) / dpi.DpiScaleY;
                break;
        }

        return (left, top);
    }

    private void FinishExpand()
    {
        if (_closing) return;
        Left = _expandedLeft;
        Top = _expandedTop;
        _animating = false;
        WindowRoot.Opacity = 1;
        WindowRoot.IsHitTestVisible = true;
        UpdateLayout();
        ClampNotesToCanvas();
        HideTrayIcon();
        _dockEdge = ScreenUtil.DetectDock(this);
        UpdateEmptyHint();
        UpdateEdgeIndicator();
        ScheduleAutoHide(1.8);
    }

    private void AnimateWindowTo(double targetLeft, double targetTop, int milliseconds,
        EasingMode easingMode, Action completed)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new CubicEase { EasingMode = easingMode };
        var leftAnimation = new DoubleAnimation(targetLeft, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        var topAnimation = new DoubleAnimation(targetTop, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };

        leftAnimation.Completed += (_, _) =>
        {
            if (_closing) return;
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
            completed();
        };

        BeginAnimation(LeftProperty, leftAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    private void StopAnimations()
    {
        _animating = false;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        CollapsedTab.BeginAnimation(OpacityProperty, null);
        if (CollapsedTab.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        CollapsedTab.RenderTransform = null;
        WindowRoot.Opacity = 1;
        WindowRoot.IsHitTestVisible = true;
    }

    // -------------------------------------------------------------------- tray

    private void ShowTrayIcon()
    {
        if (_previewMode) return;
        EnsureTrayIcon();
        if (_trayIcon != null) _trayIcon.Visible = true;
    }

    private void HideTrayIcon()
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null) return;

        _trayDrawingIcon = LoadTrayIcon();
        _trayMenu = new Forms.ContextMenuStrip();
        var expand = new Forms.ToolStripMenuItem("展开便签板");
        expand.Click += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ExpandFromTray));
        var add = new Forms.ToolStripMenuItem("新建便签");
        add.Click += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            ExpandFromTray();
            AddNote();
        }));
        var exit = new Forms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
        _trayMenu.Items.Add(expand);
        _trayMenu.Items.Add(add);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add(exit);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayDrawingIcon,
            Text = "CoTab 便签板",
            ContextMenuStrip = _trayMenu,
            Visible = false
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ExpandFromTray));
        };
        _trayIcon.DoubleClick += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ExpandFromTray));
    }

    private Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource?.Stream != null) return new Drawing.Icon(resource.Stream);
        }
        catch
        {
            // Fall through to the executable icon.
        }

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
                return Drawing.Icon.ExtractAssociatedIcon(path) ?? (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
        catch
        {
            // Fall through to the system icon.
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private void ExpandFromTray()
    {
        if (_isCollapsed) ExpandPanel();
        if (!IsVisible) Show();
        Activate();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayDrawingIcon?.Dispose();
        _trayDrawingIcon = null;
    }

    // ---------------------------------------------------------------- keyboard

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ConfirmOverlay.Visibility == Visibility.Visible)
            {
                HideConfirm();
                e.Handled = true;
            }
            else if (_settingsOpen)
            {
                HideSettings();
                e.Handled = true;
            }
            else if (_notes.Any(n => n.IsEditing))
            {
                foreach (var note in _notes.Where(n => n.IsEditing).ToList()) note.EndEdit();
                e.Handled = true;
            }
            else if (_isCollapsed)
            {
                ExpandPanel();
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.N)
            {
                AddNote();
                e.Handled = true;
            }
            else if (e.Key == Key.OemComma)
            {
                ShowSettings();
                e.Handled = true;
            }
        }
    }

    // ---------------------------------------------------------------- persistence

    private void QueueSave()
    {
        if (_previewMode) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (_previewMode) return;
        foreach (var note in _notes.Where(n => n.IsEditing).ToList())
            note.EndEdit();

        if (!_isCollapsed)
        {
            _state.Window.Left = Left;
            _state.Window.Top = Top;
            _state.Window.Width = Width;
            _state.Window.Height = Height;
            _state.Window.HasPosition = true;
        }
        else
        {
            _state.Window.Left = _expandedLeft;
            _state.Window.Top = _expandedTop;
            _state.Window.Width = _expandedWidth;
            _state.Window.Height = _expandedHeight;
            _state.Window.HasPosition = true;
        }

        StateStore.Save(_state);
    }

    // ---------------------------------------------------------------- preview hooks

    public FrameworkElement PreviewRoot => WindowRoot;

    public void ApplyPreviewScene(string scene)
    {
        switch ((scene ?? string.Empty).ToLowerInvariant())
        {
            case "empty":
                foreach (var note in _notes.ToList())
                {
                    NotesCanvas.Children.Remove(note);
                }
                _notes.Clear();
                break;
            case "settings":
                ShowSettingsPreview();
                break;
            case "confirm":
                ConfirmOverlay.Visibility = Visibility.Visible;
                break;
            case "editing":
                if (_notes.Count > 0) _notes[0].BeginEdit();
                break;
            case "collapsed":
                PanelContainer.Visibility = Visibility.Collapsed;
                ResizeLayer.Visibility = Visibility.Collapsed;
                CollapsedTab.Visibility = Visibility.Visible;
                ConfigureCollapsedTab(DockEdge.Right);
                CollapsedTab.Width = CollapsedTabThicknessDip;
                CollapsedTab.Height = CollapsedTabLengthDip;
                CollapsedTab.HorizontalAlignment = HorizontalAlignment.Right;
                CollapsedTab.VerticalAlignment = VerticalAlignment.Top;
                // Representative of the default top-right panel location rather
                // than a tab artificially parked at the middle of the edge.
                CollapsedTab.Margin = new Thickness(0, 86, 0, 0);
                break;
        }

        UpdateEmptyHint();
    }

    private void ShowSettingsPreview()
    {
        _settingsOpen = true;
        RefreshSettingsUi();
        SettingsOverlay.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;
    }
}
