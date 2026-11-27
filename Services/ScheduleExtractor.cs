using System.Globalization;
using System.Text.RegularExpressions;

namespace CoTab.Services;

/// <summary>
/// Lightweight local date/time extraction for Chinese and English sticky-note text.
/// It deliberately stays deterministic and offline: AI extraction is an optional fallback.
/// </summary>
public static class ScheduleExtractor
{
    public sealed record Draft(string Title, DateTimeOffset StartAt, int ReminderMinutes);

    private static readonly Regex FullDate = new(
        @"(?<!\d)(?<y>\d{4})\s*[-/.年]\s*(?<m>\d{1,2})\s*[-/.月]\s*(?<d>\d{1,2})\s*[日号]?",
        RegexOptions.Compiled);

    private static readonly Regex MonthDay = new(
        @"(?<!\d)(?<m>\d{1,2})\s*月\s*(?<d>\d{1,2})\s*[日号]?",
        RegexOptions.Compiled);

    private static readonly Regex SlashDate = new(
        @"(?<!\d)(?<m>\d{1,2})\s*[/.]\s*(?<d>\d{1,2})(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex RelativeDate = new(
        @"(?<rel>大后天|后天|明天|今天|今晚|明晚|明早|今早|今早|今晚)",
        RegexOptions.Compiled);

    private static readonly Regex Weekday = new(
        @"(?<prefix>下下|下|本|这)?\s*(?:周|星期|礼拜)\s*(?<day>[一二三四五六日天])",
        RegexOptions.Compiled);

    private static readonly Regex ColonTime = new(
        @"(?<!\d)(?<h>\d{1,2})\s*[:：]\s*(?<m>\d{2})(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex ChineseTime = new(
        @"(?<h>\d{1,2})\s*(?:点|时)\s*(?<half>半)?\s*(?<m>\d{1,2})?\s*分?",
        RegexOptions.Compiled);

    private static readonly Regex Period = new(
        @"(?<p>凌晨|早上|上午|中午|下午|傍晚|晚上|夜里|夜间|今晚|明晚|明早)",
        RegexOptions.Compiled);

    private static readonly Regex Reminder = new(
        @"提前\s*(?<n>\d{1,3})\s*(?<unit>分钟|分|小时|钟头)",
        RegexOptions.Compiled);

    private static readonly Regex TitleLeadDate = new(
        @"^\s*(?:(?:大后天|后天|明天|今天|今晚|明晚|明早|今早)|(?:下下|下|本|这)?\s*(?:周|星期|礼拜)\s*[一二三四五六日天]|(?:\d{4})\s*[-/.年]\s*\d{1,2}\s*[-/.月]\s*\d{1,2}\s*[日号]?|\d{1,2}\s*月\s*\d{1,2}\s*[日号]?|\d{1,2}\s*[/.]\s*\d{1,2})\s*",
        RegexOptions.Compiled);

    private static readonly Regex TitleLeadTime = new(
        @"^\s*(?:凌晨|早上|上午|中午|下午|傍晚|晚上|夜里|夜间)?\s*\d{1,2}\s*(?:[:：]\s*\d{2}|点\s*(?:半|\d{1,2}\s*分?)?|时\s*(?:半|\d{1,2}\s*分?)?)\s*",
        RegexOptions.Compiled);

    private static readonly Regex TitleReminderTail = new(
        @"\s*提前\s*\d{1,3}\s*(?:分钟|分|小时|钟头)\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<Draft> Extract(string? text, DateTimeOffset now, int defaultReminderMinutes)
    {
        var drafts = new List<Draft>();
        if (string.IsNullOrWhiteSpace(text)) return drafts;
        if (text.Length > 20_000) text = text[..20_000];

        foreach (var raw in text.Split('\n'))
        {
            var line = CleanLine(raw);
            if (line.Length < 2 || line.Length > 200) continue;
            if (!LooksTemporal(line)) continue;

            var reminder = ParseReminderMinutes(line, defaultReminderMinutes);
            var date = ParseDate(line, now, out var dateFound);
            var time = ParseTime(line, out var periodHint);
            if (!dateFound && time is null) continue;

            var start = BuildStart(date, time, periodHint, now, dateFound);
            if (start is null) continue;
            if (start.Value < now.AddDays(-1)) continue;
            if (start.Value > now.AddYears(5)) continue;

            var title = CleanTitle(line);
            if (title.Length > 100) title = title[..100].Trim();
            if (title.Length == 0) title = "日程";

            if (drafts.Any(d => d.StartAt == start.Value && d.Title == title)) continue;
            drafts.Add(new Draft(title, start.Value, reminder));
        }

        return drafts.OrderBy(d => d.StartAt).Take(12).ToList();
    }

    public static bool LooksTemporal(string text)
    {
        return RelativeDate.IsMatch(text) || Weekday.IsMatch(text) ||
               FullDate.IsMatch(text) || MonthDay.IsMatch(text) ||
               SlashDate.IsMatch(text) || ColonTime.IsMatch(text) ||
               ChineseTime.IsMatch(text) || Period.IsMatch(text);
    }

    private static string CleanTitle(string line)
    {
        var title = TitleLeadDate.Replace(line, string.Empty, 1);
        title = TitleLeadTime.Replace(title, string.Empty, 1);
        title = TitleReminderTail.Replace(title, string.Empty, 1);
        title = title.Trim(' ', '-', '*', '>', '#', '•', '·', '\t', ':', '：');
        return string.IsNullOrWhiteSpace(title) ? "日程" : title;
    }

    private static string CleanLine(string raw)
    {
        var line = raw.Replace('：', ':').Replace('　', ' ').Trim();
        line = Regex.Replace(line, @"^\s*(?:[-*+]\s*)?(?:\[[ xX]\]\s*)?", string.Empty);
        line = Regex.Replace(line, @"^\s*#{1,6}\s*", string.Empty);
        line = Regex.Replace(line, @"\s+", " ");
        return line;
    }

    private static DateTime? ParseDate(string line, DateTimeOffset now, out bool found)
    {
        found = false;
        var today = now.Date;

        var full = FullDate.Match(line);
        if (full.Success &&
            int.TryParse(full.Groups["y"].Value, out var y) &&
            int.TryParse(full.Groups["m"].Value, out var mo) &&
            int.TryParse(full.Groups["d"].Value, out var d))
        {
            found = true;
            return SafeDate(y, mo, d);
        }

        var md = MonthDay.Match(line);
        if (md.Success &&
            int.TryParse(md.Groups["m"].Value, out var mm) &&
            int.TryParse(md.Groups["d"].Value, out var dd))
        {
            found = true;
            var date = SafeDate(today.Year, mm, dd);
            if (date is not null && date.Value < today.AddDays(-30)) date = SafeDate(today.Year + 1, mm, dd);
            return date;
        }

        var slash = SlashDate.Match(line);
        if (slash.Success &&
            int.TryParse(slash.Groups["m"].Value, out var sm) &&
            int.TryParse(slash.Groups["d"].Value, out var sd) &&
            sm is >= 1 and <= 12 && sd is >= 1 and <= 31)
        {
            found = true;
            var date = SafeDate(today.Year, sm, sd);
            if (date is not null && date.Value < today.AddDays(-30)) date = SafeDate(today.Year + 1, sm, sd);
            return date;
        }

        var relative = RelativeDate.Match(line);
        if (relative.Success)
        {
            found = true;
            return relative.Groups["rel"].Value switch
            {
                "今天" or "今晚" or "今早" => today,
                "明天" or "明晚" or "明早" => today.AddDays(1),
                "后天" => today.AddDays(2),
                "大后天" => today.AddDays(3),
                _ => today
            };
        }

        var weekday = Weekday.Match(line);
        if (weekday.Success)
        {
            found = true;
            var target = weekday.Groups["day"].Value switch
            {
                "一" => DayOfWeek.Monday,
                "二" => DayOfWeek.Tuesday,
                "三" => DayOfWeek.Wednesday,
                "四" => DayOfWeek.Thursday,
                "五" => DayOfWeek.Friday,
                "六" => DayOfWeek.Saturday,
                "日" or "天" => DayOfWeek.Sunday,
                _ => DayOfWeek.Monday
            };
            var current = today.DayOfWeek;
            var delta = ((int)target - (int)current + 7) % 7;
            var prefix = weekday.Groups["prefix"].Value;
            if (prefix == "下") delta += 7;
            else if (prefix == "下下") delta += 14;
            else if (prefix is not ("本" or "这") && delta == 0) delta = 7;
            return today.AddDays(delta);
        }

        return null;
    }

    private static (int Hour, int Minute)? ParseTime(string line, out string periodHint)
    {
        periodHint = string.Empty;
        var periodMatch = Period.Match(line);
        if (periodMatch.Success) periodHint = periodMatch.Groups["p"].Value;
        else if (line.Contains("下午", StringComparison.Ordinal)) periodHint = "下午";
        else if (line.Contains("晚上", StringComparison.Ordinal)) periodHint = "晚上";
        else if (line.Contains("上午", StringComparison.Ordinal)) periodHint = "上午";

        var colon = ColonTime.Match(line);
        if (colon.Success &&
            int.TryParse(colon.Groups["h"].Value, out var ch) &&
            int.TryParse(colon.Groups["m"].Value, out var cm) &&
            ch is >= 0 and <= 23 && cm is >= 0 and <= 59)
        {
            return (ch, cm);
        }

        var chinese = ChineseTime.Match(line);
        if (chinese.Success && int.TryParse(chinese.Groups["h"].Value, out var hh) && hh is >= 0 and <= 23)
        {
            var minute = chinese.Groups["half"].Value == "半"
                ? 30
                : int.TryParse(chinese.Groups["m"].Value, out var mm) && mm is >= 0 and <= 59 ? mm : 0;
            return (hh, minute);
        }

        return null;
    }

    private static DateTimeOffset? BuildStart(DateTime? date, (int Hour, int Minute)? time, string periodHint,
        DateTimeOffset now, bool dateFound)
    {
        var today = now.Date;
        var day = date ?? today;
        var hour = time?.Hour;
        var minute = time?.Minute ?? 0;

        if (hour is null)
        {
            if (!dateFound) return null;
            hour = periodHint switch
            {
                "明早" or "今早" or "早上" => 8,
                "上午" => 10,
                "中午" => 12,
                "下午" => 15,
                "傍晚" => 18,
                "晚上" or "今晚" or "明晚" or "夜里" or "夜间" => 20,
                "凌晨" => 6,
                _ => 9
            };
        }
        else
        {
            var h = hour.Value;
            if (periodHint is "下午" or "傍晚" or "晚上" or "夜里" or "夜间" or "今晚" or "明晚")
            {
                if (h < 12) h += 12;
            }
            else if (periodHint is "中午" && h is >= 1 and <= 10)
            {
                h += 12;
            }
            else if (periodHint is "凌晨" && h == 12)
            {
                h = 0;
            }
            hour = h;
        }

        if (hour is < 0 or > 23) return null;
        var local = new DateTime(day.Year, day.Month, day.Day, hour.Value, minute, 0, DateTimeKind.Local);
        if (!dateFound && local < now.LocalDateTime)
            local = local.AddDays(1);

        return new DateTimeOffset(local);
    }

    private static int ParseReminderMinutes(string line, int fallback)
    {
        var match = Reminder.Match(line);
        if (!match.Success || !int.TryParse(match.Groups["n"].Value, out var n)) return fallback;
        var minutes = match.Groups["unit"].Value is "小时" or "钟头" ? n * 60 : n;
        return Math.Clamp(minutes, 1, 120);
    }

    private static DateTime? SafeDate(int year, int month, int day)
    {
        if (year is < 1900 or > 2200 || month is < 1 or > 12 || day < 1) return null;
        if (day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day);
    }
}
