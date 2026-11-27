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

    public ReminderWindow(ScheduleEvent scheduleEvent)
    {
        InitializeComponent();
        _event = scheduleEvent;
        TitleText.Text = scheduleEvent.Title;
        TimeText.Text = $"{scheduleEvent.StartAt:M月d日 HH:mm} · 提前 {scheduleEvent.ReminderMinutes} 分钟";
        DetailText.Text = string.IsNullOrWhiteSpace(scheduleEvent.BoardId)
            ? "来自便签板中的日程识别。"
            : "来自分类板块中的日程识别。";
        Opacity = 0;
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
