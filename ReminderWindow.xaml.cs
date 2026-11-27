using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoTab.Helpers;
using CoTab.Models;
using CoTab.Services;

namespace CoTab;

public partial class ReminderWindow : Window
{
    private readonly ScheduleEvent _event;

    public event EventHandler? Snoozed;

    public ReminderWindow(ScheduleEvent scheduleEvent, SettingsModel settings)
    {
        InitializeComponent();
        _event = scheduleEvent;
        TitleText.Text = scheduleEvent.Title;
        var when = scheduleEvent.HasTime
            ? scheduleEvent.StartAt.ToString("M月d日 HH:mm")
            : $"{scheduleEvent.StartAt:M月d日} 全天";
        TimeText.Text = $"{when} · 提前 {scheduleEvent.ReminderMinutes} 分钟";
        DetailText.Text = string.IsNullOrWhiteSpace(scheduleEvent.BoardId)
            ? "来自便签板中的日程识别。"
            : "来自分类板块中的日程识别。";
        Opacity = 0;
        ApplyTheme(settings);
    }

    private void ApplyTheme(SettingsModel settings)
    {
        var panel = ColorUtil.Parse(settings.PanelColor);
        var accent = ColorUtil.Parse(settings.AiAccentColor);
        var dark = ColorUtil.Luminance(panel) < 0.46;
        var text = dark ? Color.FromRgb(243, 244, 247) : Color.FromRgb(29, 30, 34);
        var muted = dark ? Color.FromRgb(156, 163, 175) : Color.FromRgb(104, 108, 118);
        var accentText = ColorUtil.Luminance(accent) > 0.55
            ? Color.FromRgb(29, 30, 34)
            : Colors.White;

        ShellBorder.Background = ColorUtil.Brush(ColorUtil.WithAlpha(panel, 0.97));
        ShellBorder.BorderBrush = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.34));
        BadgeBorder.Background = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.16));
        BadgeText.Foreground = ColorUtil.Brush(accent);
        HeaderText.Foreground = ColorUtil.Brush(text);
        TitleText.Foreground = ColorUtil.Brush(text);
        TimeText.Foreground = ColorUtil.Brush(muted);
        DetailText.Foreground = ColorUtil.Brush(ColorUtil.Blend(muted, text, 0.35));
        SnoozeButton.Background = ColorUtil.Brush(ColorUtil.WithAlpha(panel, 0.45));
        SnoozeButton.Foreground = ColorUtil.Brush(text);
        SnoozeButton.BorderBrush = ColorUtil.Brush(ColorUtil.WithAlpha(text, 0.18));
        DismissButton.Background = ColorUtil.Brush(accent);
        DismissButton.Foreground = ColorUtil.Brush(accentText);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Left = Math.Max(work.Left + 16, work.Right - ActualWidth - 24);
        Top = Math.Max(work.Top + 16, work.Bottom - ActualHeight - 24);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        Activate();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        Snoozed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
