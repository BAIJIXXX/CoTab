using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CoTab.Helpers;
using CoTab.Models;
using CoTab.Services;

namespace CoTab;

public partial class MainWindow
{
    private string? _pendingAiSuggestion;
    private DateTimeOffset _lastSuggestionAt = DateTimeOffset.MinValue;

    private bool AiEnabled => _state.Settings.AiEnabled;

    private void StartAiServices()
    {
        if (_previewMode || !AiEnabled) return;

        if (_schedule.SyncAll())
            QueueSave();

        StartReminderTimer();
        UpdateAiPageHint();
        if (!_state.Settings.AiCollapsed)
            ShowAssistant(openSettings: !HasAiConfiguration(), activate: false);
    }

    private bool HasAiConfiguration() =>
        !string.IsNullOrWhiteSpace(_state.Settings.AiApiKey) &&
        !string.IsNullOrWhiteSpace(_state.Settings.AiModel);

    private void StartReminderTimer()
    {
        if (_previewMode) return;
        if (_reminderTimer == null)
        {
            _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _reminderTimer.Tick += ReminderTimer_Tick;
        }
        _reminderTimer.Start();
    }

    private void StopAiServices()
    {
        _scheduleScanTimer?.Stop();
        _reminderTimer?.Stop();
        _assistantWindow?.Close();
        _assistantWindow = null;
        _reminderWindow?.Close();
        _reminderWindow = null;
    }

    public void SetAiEnabled(bool enabled)
    {
        if (_state.Settings.AiEnabled == enabled) return;
        _state.Settings.AiEnabled = enabled;
        QueueSave();
        if (enabled)
        {
            StartAiServices();
            UpdateAiPageHint();
            if (!_state.Settings.AiCollapsed)
                ShowAssistant(openSettings: !HasAiConfiguration(), activate: true);
        }
        else
        {
            StopAiServices();
            UpdateAiPageHint();
        }
    }

    private void ShowAssistant(bool openSettings = false, bool activate = true)
    {
        if (_previewMode || !AiEnabled) return;

        if (_state.Settings.AiCollapsed)
        {
            _state.Settings.AiCollapsed = false;
            QueueSave();
        }

        if (_assistantWindow == null)
        {
            _assistantWindow = new AssistantWindow(_state, _schedule, this)
            {
                Owner = this,
                ShowInTaskbar = false
            };
            _assistantWindow.Closed += (_, _) => _assistantWindow = null;
            _assistantWindow.ApplyTheme();
            _assistantWindow.ApplyCollapsedState();
            _assistantWindow.FollowMain();
        }
        else
        {
            // A window that was previously folded still has ShellBorder collapsed;
            // bringing it back must restore the shell before Show().
            _assistantWindow.ApplyCollapsedState();
            _assistantWindow.FollowMain();
        }

        if (openSettings) _assistantWindow.ShowSettingsTab();
        if (!_assistantWindow.IsVisible)
        {
            _assistantWindow.FollowMain();
            _assistantWindow.Show();
        }
        if (activate)
        {
            _assistantWindow.FollowMain();
            _assistantWindow.Activate();
        }
        if (_pendingAiSuggestion != null)
        {
            var suggestion = _pendingAiSuggestion;
            _pendingAiSuggestion = null;
            _assistantWindow.AddSuggestion(suggestion);
        }
        UpdateAiPageHint();
    }

    private void HideAssistant()
    {
        _assistantWindow?.Hide();
        UpdateAiPageHint();
    }

    private void ShowAssistantAfterRestore()
    {
        if (!AiEnabled) return;
        if (_state.Settings.AiCollapsed)
        {
            UpdateAiPageHint();
            return;
        }
        ShowAssistant(activate: false);
    }

    public bool IsCollapsedForAi => _isCollapsed;
    public bool IsHiddenToTrayForAi => _hiddenToTray;
    public AppMode CurrentMode => _state.Settings.Mode;

    public void PersistAiSettings() => SaveNow();

    public bool IsAiPageHintVisibleForProbe => AiPageHint.Visibility == Visibility.Visible;

    public void UpdateAiPageHint()
    {
        var visible = AiEnabled && _state.Settings.AiCollapsed &&
                      !_hiddenToTray && !_isCollapsed &&
                      (_previewMode || IsVisible);
        AiPageHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        var settings = _state.Settings;
        var panel = ColorUtil.Parse(settings.PanelColor, Colors.White);
        var accent = ColorUtil.Parse(settings.AiAccentColor, Color.FromRgb(255, 214, 107));
        var ink = ColorUtil.Parse(settings.CloseColor, Color.FromRgb(59, 60, 66));
        var opacity = Math.Clamp(settings.PanelOpacity, 0.18, 1.0);
        var edge = ColorUtil.WithAlpha(ink, 0.22);
        var onRight = _assistantWindow?.IsOnRightSide ?? ComputeAiSideIsRight();

        AiPageHint.HorizontalAlignment = onRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        AiPageHint.Margin = onRight
            ? new Thickness(0, 16, 2, 16)
            : new Thickness(2, 16, 0, 16);
        AiPageFront.CornerRadius = onRight
            ? new CornerRadius(4, 5, 5, 4)
            : new CornerRadius(5, 4, 4, 5);
        AiPageAccent.HorizontalAlignment = HorizontalAlignment.Center;
        AiPageAccent.VerticalAlignment = VerticalAlignment.Center;
        AiPageAccent.Margin = new Thickness(0);

        AiPageFront.Background = ColorUtil.Brush(ColorUtil.WithAlpha(panel, opacity));
        AiPageFront.BorderBrush = ColorUtil.Brush(edge);
        AiPageAccent.Background = ColorUtil.Brush(accent);
    }

    public bool ProbeAiPageHintHitAt(Point point)
    {
        var hit = InputHitTest(point) as DependencyObject;
        while (hit != null)
        {
            if (ReferenceEquals(hit, AiPageHint)) return true;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return false;
    }

    private bool ComputeAiSideIsRight()
    {
        var work = ScreenUtil.GetWorkAreaDip(this);
        const double panelInset = 16;
        const double panelGap = 4;
        const double shellMargin = 10;
        const double assistantWidth = 360;
        var panelRight = Left + Width - panelInset;
        return panelRight + panelGap - shellMargin + assistantWidth <= work.Right - 4;
    }

    private void AiPageHint_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!AiEnabled) return;
        _state.Settings.AiCollapsed = false;
        PersistAiSettings();
        UpdateAiPageHint();
        ShowAssistant(activate: true);
        e.Handled = true;
    }

    private void OpenAssistantButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!AiEnabled)
        {
            SetAiEnabled(true);
            return;
        }
        ShowAssistant(activate: true);
    }

    private void AiEnabledSwitch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_uiUpdating) return;
        var enabled = AiEnabledSwitch.IsChecked == true;
        SetAiEnabled(enabled);
        if (AiEnabledSwitch.IsChecked != enabled) AiEnabledSwitch.IsChecked = enabled;
    }

    private void QueueScheduleScan()
    {
        if (_previewMode || !AiEnabled || !_state.Settings.AiAutoSchedule) return;
        if (_scheduleScanTimer == null)
        {
            _scheduleScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            _scheduleScanTimer.Tick += ScheduleScanTimer_Tick;
        }
        _scheduleScanTimer.Stop();
        _scheduleScanTimer.Start();
    }

    public void RescanSchedule()
    {
        if (_previewMode) return;
        if (_schedule.SyncAll())
        {
            QueueSave();
            _assistantWindow?.RefreshSchedule();
        }
    }

    private void ScheduleScanTimer_Tick(object? sender, EventArgs e)
    {
        _scheduleScanTimer?.Stop();
        if (_closing || !AiEnabled) return;
        var changed = _schedule.SyncAll();
        if (!changed) return;

        QueueSave();
        _assistantWindow?.RefreshSchedule();
        if (!_state.Settings.AiAutoSuggestions) return;
        if (DateTimeOffset.Now - _lastSuggestionAt < TimeSpan.FromSeconds(25)) return;
        var suggestion = _schedule.BuildChangeSuggestion();
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        _lastSuggestionAt = DateTimeOffset.Now;
        OnScheduleSuggestion(suggestion);
    }

    private void ReminderTimer_Tick(object? sender, EventArgs e)
    {
        if (_closing || !AiEnabled) return;
        if (_reminderWindow != null) return;

        var due = _schedule.TakeDue(DateTimeOffset.Now);
        if (due == null) return;

        _schedule.MarkNotified(due);
        QueueSave();
        _assistantWindow?.RefreshSchedule();
        ShowReminder(due);
    }

    private void ShowReminder(ScheduleEvent scheduleEvent)
    {
        var reminder = new ReminderWindow(scheduleEvent, _state.Settings);
        _reminderWindow = reminder;
        reminder.Snoozed += (_, _) =>
        {
            _schedule.Snooze(scheduleEvent);
            QueueSave();
            _assistantWindow?.RefreshSchedule();
        };
        reminder.Closed += (_, _) =>
        {
            if (ReferenceEquals(_reminderWindow, reminder)) _reminderWindow = null;
        };
        reminder.Show();
    }

    private void OnScheduleSuggestion(string text)
    {
        if (!AiEnabled || string.IsNullOrWhiteSpace(text)) return;
        if (_assistantWindow is { IsVisible: true })
            _assistantWindow.AddSuggestion(text);
        else
            _pendingAiSuggestion = text;
    }

    // ---------------------------------------------------------------- MCP-style tools

    public string AiGetStateJson()
    {
        var payload = new
        {
            ok = true,
            mode = IsMultiBoard ? "multi" : "minimal",
            activeBoard = _activeBoard == null ? null : new { id = _activeBoard.Id, title = _activeBoard.Title },
            boards = _state.Boards.Select(b => new
            {
                id = b.Id,
                title = b.Title,
                noteCount = b.Notes.Count,
                notes = b.Notes.Take(8).Select(n => new { id = n.Id, text = Snippet(n.Text) })
            }),
            notes = _state.Notes.Take(12).Select(n => new { id = n.Id, text = Snippet(n.Text) }),
            schedule = _schedule.All.Take(10).Select(e => new
            {
                id = e.Id,
                title = e.Title,
                startAt = e.StartAt.ToString("yyyy-MM-dd HH:mm"),
                noteId = e.NoteId,
                boardId = e.BoardId
            })
        };
        return JsonSerializer.Serialize(payload);
    }

    public string AiCreateBoard(string? title, string? color)
    {
        if (!IsMultiBoard)
            return Error("当前是极简模式，没有子板。请直接创建便签，或让用户在设置中切换多板模式。");
        if (_isCollapsed) ExpandPanel();
        if (_settingsOpen) HideSettings();

        title = string.IsNullOrWhiteSpace(title) ? $"板块 {_state.Boards.Count + 1}" : title.Trim();
        if (title.Length > 30) title = title[..30];
        var parsedColor = ColorUtil.Parse(color, DefaultBoardColor());
        var board = new SubBoardModel { Title = title, Color = ColorUtil.ToHex(parsedColor) };
        _state.Boards.Add(board);
        BuildBoardHome();
        BuildBoardRail();
        UpdateEmptyHint();
        QueueSave();
        Dispatcher.BeginInvoke(new Action(() => BoardsScroll.ScrollToEnd()), DispatcherPriority.Background);
        return JsonSerializer.Serialize(new { ok = true, id = board.Id, title = board.Title, color = board.Color });
    }

    public string AiCreateNote(string? content, string? boardId, bool markdown)
    {
        content ??= string.Empty;
        if (content.Length > 20_000) content = content[..20_000];
        if (string.IsNullOrWhiteSpace(content))
            return Error("便签内容不能为空。");
        if (_isCollapsed) ExpandPanel();
        if (_settingsOpen) HideSettings();

        List<NoteModel> owner;
        SubBoardModel? board = null;
        if (IsMultiBoard)
        {
            if (!string.IsNullOrWhiteSpace(boardId))
                board = _state.Boards.FirstOrDefault(b =>
                    string.Equals(b.Id, boardId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Title, boardId, StringComparison.OrdinalIgnoreCase));
            board ??= _activeBoard;
            if (board == null && _state.Boards.Count == 1) board = _state.Boards[0];
            if (board == null)
            {
                board = new SubBoardModel
                {
                    Title = "AI 便签",
                    Color = ColorUtil.ToHex(DefaultBoardColor())
                };
                _state.Boards.Add(board);
            }
            owner = board.Notes;
        }
        else
        {
            owner = _state.Notes;
        }

        var canvas = board == null || IsBoardOpen ? ActiveCanvas : null;
        var canvasWidth = canvas != null && canvas.ActualWidth > 0 ? canvas.ActualWidth : Math.Max(180, Width - 56);
        var canvasHeight = canvas != null && canvas.ActualHeight > 0 ? canvas.ActualHeight : Math.Max(140, Height - 120);
        var note = new NoteModel
        {
            Text = content.Trim(),
            FillColor = _state.Settings.NoteFillColor,
            FontColor = _state.Settings.NoteFontColor,
            Markdown = markdown,
            Width = Math.Clamp(184, 140, Math.Max(140, canvasWidth - 8)),
            Height = Math.Clamp(184, 110, Math.Max(110, canvasHeight - 8))
        };
        note.X = Math.Max(0, (canvasWidth - note.Width) / 2);
        note.Y = Math.Max(0, (canvasHeight - note.Height) / 2);
        owner.Add(note);

        var displayed = !IsMultiBoard || (board != null && ReferenceEquals(board, _activeBoard));
        if (displayed)
        {
            ReloadNotes();
            AnimateVisibleNotesIn();
        }
        else
        {
            BuildBoardHome();
            BuildBoardRail();
            UpdateEmptyHint();
        }
        UpdateBoardHeader();
        QueueSave();
        if (AiEnabled && _state.Settings.AiAutoSchedule)
        {
            _schedule.SyncAll();
            QueueSave();
        }
        _assistantWindow?.RefreshSchedule();
        return JsonSerializer.Serialize(new
        {
            ok = true,
            noteId = note.Id,
            boardId = board?.Id,
            boardTitle = board?.Title,
            mode = IsMultiBoard ? "multi" : "minimal"
        });
    }

    public string AiOpenBoard(string? boardIdOrTitle)
    {
        if (!IsMultiBoard) return Error("当前是极简模式，没有子板。");
        if (string.IsNullOrWhiteSpace(boardIdOrTitle)) return Error("请提供板块 id 或标题。");
        var board = _state.Boards.FirstOrDefault(b =>
            string.Equals(b.Id, boardIdOrTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Title, boardIdOrTitle, StringComparison.OrdinalIgnoreCase));
        if (board == null) return Error("没有找到这个板块。请先调用 get_state 查看现有板块。");
        OpenBoard(board);
        return JsonSerializer.Serialize(new { ok = true, boardId = board.Id, title = board.Title });
    }

    public string AiListSchedule()
    {
        var payload = _schedule.All.Select(e => new
        {
            id = e.Id,
            title = e.Title,
            startAt = e.StartAt.ToString("yyyy-MM-dd HH:mm"),
            reminderMinutes = e.ReminderMinutes,
            noteId = e.NoteId,
            boardId = e.BoardId
        });
        return JsonSerializer.Serialize(new { ok = true, events = payload });
    }

    private static string Error(string message) => JsonSerializer.Serialize(new { ok = false, error = message });

    private static string Snippet(string? text)
    {
        var value = (text ?? string.Empty).Replace('\n', ' ').Trim();
        return value.Length > 120 ? value[..120] + "…" : value;
    }
}
