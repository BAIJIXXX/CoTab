using System.Text.Json.Serialization;

namespace CoTab.Models;

public sealed class AppState
{
    public int Version { get; set; } = 2;
    public SettingsModel Settings { get; set; } = new();
    public List<NoteModel> Notes { get; set; } = new();
    public List<SubBoardModel> Boards { get; set; } = new();
    public WindowModel Window { get; set; } = new();
}

public enum AppMode
{
    Minimal,
    MultiBoard
}

public sealed class SettingsModel
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppMode Mode { get; set; } = AppMode.Minimal;

    public string PanelColor { get; set; } = "#FFFFFF";
    public double PanelOpacity { get; set; } = 0.82;
    public bool BoardRailVisible { get; set; } = true;

    public string SettingsColor { get; set; } = "#3B3C42";
    public string CloseColor { get; set; } = "#3B3C42";
    public string AddColor { get; set; } = "#3B3C42";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PanelBackgroundStyle BackgroundStyle { get; set; } = PanelBackgroundStyle.Blank;

    public string? BackgroundImagePath { get; set; }
    public double BackgroundImageOpacity { get; set; } = 0.9;

    public bool AutoHide { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;

    public bool TrashEnabled { get; set; } = true;
    public bool ConfirmTrashDelete { get; set; } = true;
    public bool TrayHintShown { get; set; }

    public string NoteFillColor { get; set; } = "#FFF1B8";
    public string NoteFontColor { get; set; } = "#3D382B";
}

public enum PanelBackgroundStyle
{
    Blank,
    Dots,
    Cross,
    Image
}

public sealed class WindowModel
{
    public bool HasPosition { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 600;
}

public sealed class SubBoardModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新板块";
    public string Color { get; set; } = "#E9E3D5";
    public List<NoteModel> Notes { get; set; } = new();
}

public sealed class NoteModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 184;
    public double Height { get; set; } = 184;
    public string Text { get; set; } = string.Empty;
    public string FillColor { get; set; } = "#FFF1B8";
    public string FontColor { get; set; } = "#3D382B";
    public bool Markdown { get; set; } = true;
}
