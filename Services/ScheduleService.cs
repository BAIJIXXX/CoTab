using CoTab.Models;

namespace CoTab.Services;

/// <summary>
/// Keeps the calendar entries in sync with note text and decides when a reminder is due.
/// All extraction runs locally; the AI layer can create the same events through its tools.
/// </summary>
public sealed class ScheduleService
{
    private readonly AppState _state;

    public ScheduleService(AppState state) => _state = state;

    public event Action<string>? SuggestionReady;

    public IReadOnlyList<ScheduleEvent> All =>
        _state.ScheduleEvents.OrderBy(e => e.StartAt).ToList();

    public IReadOnlyList<ScheduleEvent> ForNote(string noteId) =>
        _state.ScheduleEvents.Where(e => string.Equals(e.NoteId, noteId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.StartAt).ToList();

    public bool SyncNote(NoteModel note, string? boardId)
    {
        if (!_state.Settings.AiAutoSchedule) return false;
        var drafts = ScheduleExtractor.Extract(note.Text, DateTimeOffset.Now,
            _state.Settings.AiReminderMinutes);

        var existing = ForNote(note.Id);
        if (existing.Count == drafts.Count &&
            existing.Zip(drafts).All(pair =>
                pair.First.StartAt == pair.Second.StartAt &&
                pair.First.Title == pair.Second.Title &&
                pair.First.HasTime == pair.Second.HasTime &&
                pair.First.ReminderMinutes == pair.Second.ReminderMinutes &&
                string.Equals(pair.First.BoardId, boardId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _state.ScheduleEvents.RemoveAll(e => string.Equals(e.NoteId, note.Id, StringComparison.OrdinalIgnoreCase));
        foreach (var draft in drafts)
        {
            _state.ScheduleEvents.Add(new ScheduleEvent
            {
                NoteId = note.Id,
                BoardId = boardId,
                Title = draft.Title,
                StartAt = draft.StartAt,
                HasTime = draft.HasTime,
                ReminderMinutes = draft.ReminderMinutes,
                Notified = false
            });
        }

        return true;
    }

    public bool SyncAll()
    {
        if (!_state.Settings.AiAutoSchedule) return false;
        var changed = false;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in _state.Notes)
        {
            known.Add(note.Id);
            changed |= SyncNote(note, null);
        }

        foreach (var board in _state.Boards)
        {
            foreach (var note in board.Notes)
            {
                known.Add(note.Id);
                changed |= SyncNote(note, board.Id);
            }
        }

        var removed = _state.ScheduleEvents.RemoveAll(e => !known.Contains(e.NoteId));
        return changed || removed > 0;
    }

    public bool RemoveForNote(string noteId, out bool changed)
    {
        changed = _state.ScheduleEvents.RemoveAll(e =>
            string.Equals(e.NoteId, noteId, StringComparison.OrdinalIgnoreCase)) > 0;
        return changed;
    }

    public bool RemoveForBoard(string boardId, out bool changed)
    {
        changed = _state.ScheduleEvents.RemoveAll(e =>
            string.Equals(e.BoardId, boardId, StringComparison.OrdinalIgnoreCase)) > 0;
        return changed;
    }

    public ScheduleEvent? TakeDue(DateTimeOffset now)
    {
        foreach (var evt in _state.ScheduleEvents.Where(e => !e.Notified).OrderBy(e => e.StartAt).ToList())
        {
            if (evt.SnoozeUntil is { } snooze && snooze > now) continue;

            var trigger = evt.StartAt.AddMinutes(-Math.Clamp(evt.ReminderMinutes, 1, 120));
            if (now >= trigger && now <= evt.StartAt.AddMinutes(30))
                return evt;

            // A stale event after waking up should not pop up hours late.
            if (now > evt.StartAt.AddMinutes(30)) evt.Notified = true;
        }

        return null;
    }

    public void MarkNotified(ScheduleEvent evt)
    {
        evt.Notified = true;
        evt.SnoozeUntil = null;
    }

    public void Snooze(ScheduleEvent evt, int minutes = 10)
    {
        evt.Notified = false;
        evt.SnoozeUntil = DateTimeOffset.Now.AddMinutes(Math.Clamp(minutes, 1, 120));
    }

    public string BuildChangeSuggestion()
    {
        var upcoming = _state.ScheduleEvents
            .Where(e => !e.Notified && e.StartAt >= DateTimeOffset.Now.AddMinutes(-1))
            .OrderBy(e => e.StartAt)
            .Take(3)
            .ToList();
        if (upcoming.Count == 0) return string.Empty;

        var nearest = upcoming[0];
        var delta = nearest.StartAt - DateTimeOffset.Now;
        var when = delta.TotalMinutes < 60
            ? $"{Math.Max(1, (int)delta.TotalMinutes)} 分钟后"
            : delta.TotalHours < 24
                ? $"{Math.Max(1, (int)delta.TotalHours)} 小时后"
                : nearest.HasTime
                    ? nearest.StartAt.ToString("M月d日 HH:mm")
                    : $"{nearest.StartAt:M月d日} 全天";
        var lines = new List<string>
        {
            $"我注意到日程有更新：{upcoming.Count} 个待办，最近一项「{Shorten(nearest.Title)}」在 {when}。"
        };

        var conflict = _state.ScheduleEvents
            .Where(e => !e.Notified)
            .OrderBy(e => e.StartAt)
            .FirstOrDefault(e => Math.Abs((e.StartAt - nearest.StartAt).TotalMinutes) < 30 && e.Id != nearest.Id);
        if (conflict is not null)
            lines.Add($"另外，「{Shorten(conflict.Title)}」与它相隔不到 30 分钟，可能需要留出交通/休息时间。");

        return string.Join("\n", lines);
    }

    public void RaiseSuggestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !_state.Settings.AiAutoSuggestions) return;
        SuggestionReady?.Invoke(text.Trim());
    }

    public static string FormatTime(ScheduleEvent evt)
    {
        var now = DateTimeOffset.Now;
        var day = evt.StartAt.Date == now.Date
            ? "今天"
            : evt.StartAt.Date == now.Date.AddDays(1)
                ? "明天"
                : evt.StartAt.ToString("M月d日");
        return evt.HasTime ? $"{day} {evt.StartAt:HH:mm}" : $"{day} 全天";
    }

    public static string FormatDate(ScheduleEvent evt)
    {
        var now = DateTimeOffset.Now;
        if (evt.StartAt.Date == now.Date) return "今天";
        if (evt.StartAt.Date == now.Date.AddDays(1)) return "明天";
        return evt.StartAt.Year == now.Year
            ? evt.StartAt.ToString("M月d日")
            : evt.StartAt.ToString("yyyy/M/d");
    }

    public static string FormatWeekday(ScheduleEvent evt)
    {
        var chinese = evt.StartAt.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日"
        };
        return chinese;
    }

    public static string FormatClock(ScheduleEvent evt) =>
        evt.HasTime ? evt.StartAt.ToString("HH:mm") : "全天";

    public static string Format(ScheduleEvent evt) => $"{FormatTime(evt)} · {Shorten(evt.Title)}";

    private static string Shorten(string value)
    {
        var text = value.Replace('\n', ' ').Trim();
        return text.Length > 26 ? text[..26] + "…" : text;
    }
}
