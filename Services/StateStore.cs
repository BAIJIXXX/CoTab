using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CoTab.Helpers;
using CoTab.Models;

namespace CoTab.Services;

public static class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("COTAB_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoTab");

    public static string StatePath => Path.Combine(DataDirectory, "state.json");

    public static string ImageDirectory => Path.Combine(DataDirectory, "images");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new AppState();
            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
            Normalize(state);
            return state;
        }
        catch (Exception ex)
        {
            Log("读取配置失败: " + ex);
            TryBackupBrokenState();
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        try
        {
            Normalize(state);
            Directory.CreateDirectory(DataDirectory);
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, StatePath, true);
        }
        catch (Exception ex)
        {
            Log("保存配置失败: " + ex);
        }
    }

    public static string? ImportBackgroundImage(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
            Directory.CreateDirectory(ImageDirectory);
            var destination = Path.Combine(ImageDirectory, "background" + ext.ToLowerInvariant());
            foreach (var old in Directory.EnumerateFiles(ImageDirectory, "background.*"))
            {
                if (!string.Equals(old, destination, StringComparison.OrdinalIgnoreCase))
                    File.Delete(old);
            }
            File.Copy(sourcePath, destination, true);
            return destination;
        }
        catch (Exception ex)
        {
            Log("导入背景图片失败: " + ex);
            return null;
        }
    }

    public static void DeleteBackgroundImage(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                Path.GetFullPath(path).StartsWith(Path.GetFullPath(ImageDirectory), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log("删除背景图片失败: " + ex);
        }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(Path.Combine(DataDirectory, "log.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void TryBackupBrokenState()
    {
        try
        {
            if (File.Exists(StatePath))
                File.Copy(StatePath, StatePath + ".broken", true);
        }
        catch
        {
            // ignored
        }
    }

    public static void Normalize(AppState state)
    {
        state.Settings ??= new SettingsModel();
        state.Notes ??= new List<NoteModel>();
        state.Boards ??= new List<SubBoardModel>();
        state.Window ??= new WindowModel();
        state.Version = 2;

        var s = state.Settings;
        if (!Enum.IsDefined(s.Mode)) s.Mode = AppMode.Minimal;
        s.PanelOpacity = Math.Clamp(s.PanelOpacity, 0.18, 1.0);
        s.BackgroundImageOpacity = Math.Clamp(s.BackgroundImageOpacity, 0.15, 1.0);
        s.PanelColor = NormalizeColor(s.PanelColor, "#FFFFFF");
        s.SettingsColor = NormalizeColor(s.SettingsColor, "#3B3C42");
        s.CloseColor = NormalizeColor(s.CloseColor, "#3B3C42");
        s.AddColor = NormalizeColor(s.AddColor, "#3B3C42");
        s.NoteFillColor = NormalizeColor(s.NoteFillColor, "#FFF1B8");
        s.NoteFontColor = NormalizeColor(s.NoteFontColor, "#3D382B");
        if (string.IsNullOrWhiteSpace(s.BackgroundImagePath) || !File.Exists(s.BackgroundImagePath))
        {
            s.BackgroundImagePath = null;
            if (s.BackgroundStyle == PanelBackgroundStyle.Image)
                s.BackgroundStyle = PanelBackgroundStyle.Blank;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in state.Notes)
        {
            if (note is null) continue;
            NormalizeNote(note, s, ids);
        }
        state.Notes.RemoveAll(n => n is null);

        var boardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in state.Boards)
        {
            if (board is null) continue;
            if (string.IsNullOrWhiteSpace(board.Id) || !boardIds.Add(board.Id))
            {
                board.Id = Guid.NewGuid().ToString("N");
                boardIds.Add(board.Id);
            }
            board.Title = string.IsNullOrWhiteSpace(board.Title) ? "新板块" : board.Title.Trim();
            if (board.Title.Length > 30) board.Title = board.Title[..30];
            board.Color = NormalizeColor(board.Color, "#E9E3D5");
            board.Notes ??= new List<NoteModel>();
            board.Notes.RemoveAll(n => n is null);
            foreach (var note in board.Notes)
                NormalizeNote(note, s, ids);
        }
        state.Boards.RemoveAll(b => b is null);

        var w = state.Window;
        if (double.IsNaN(w.Width) || w.Width < 320) w.Width = 400;
        if (double.IsNaN(w.Height) || w.Height < 380) w.Height = 600;
        w.Width = Math.Clamp(w.Width, 320, 1200);
        w.Height = Math.Clamp(w.Height, 380, 1400);
        if (double.IsNaN(w.Left) || double.IsInfinity(w.Left) || double.IsNaN(w.Top) || double.IsInfinity(w.Top))
            w.HasPosition = false;
    }

    private static void NormalizeNote(NoteModel note, SettingsModel s, HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(note.Id) || !ids.Add(note.Id))
        {
            note.Id = Guid.NewGuid().ToString("N");
            ids.Add(note.Id);
        }
        note.FillColor = NormalizeColor(note.FillColor, s.NoteFillColor);
        note.FontColor = NormalizeColor(note.FontColor, s.NoteFontColor);
        note.Text ??= string.Empty;
        if (note.Text.Length > 100_000) note.Text = note.Text[..100_000];
        if (double.IsNaN(note.X) || double.IsInfinity(note.X)) note.X = 0;
        if (double.IsNaN(note.Y) || double.IsInfinity(note.Y)) note.Y = 0;
        if (double.IsNaN(note.Width) || note.Width < 140) note.Width = 184;
        if (double.IsNaN(note.Height) || note.Height < 110) note.Height = 184;
        note.Width = Math.Clamp(note.Width, 140, 900);
        note.Height = Math.Clamp(note.Height, 110, 1000);
    }

    public static string NormalizeColor(string? value, string fallback)
    {
        var color = ColorUtil.Parse(value, ColorUtil.Parse(fallback, Colors.Transparent));
        return ColorUtil.ToHex(color);
    }

    public static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("打开数据目录失败: " + ex);
        }
    }
}
