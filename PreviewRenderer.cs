using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoTab.Helpers;
using CoTab.Models;
using CoTab.Services;

namespace CoTab;

/// <summary>
/// Renders the board offscreen so the UI can be checked without an interactive desktop.
/// Invoked with: CoTab.exe --preview &lt;png&gt; --scene &lt;name&gt;
/// </summary>
public static class PreviewRenderer
{
    public static void Run(string outputPath, string scene)
    {
        var state = BuildPreviewState(scene);
        var window = new MainWindow(state, previewMode: true);
        window.ApplyPreviewScene(scene);

        var root = window.PreviewRoot;
        const double boardWidth = 400;
        const double boardHeight = 600;

        root.Width = boardWidth;
        root.Height = boardHeight;
        root.Measure(new Size(boardWidth, boardHeight));
        root.Arrange(new Rect(0, 0, boardWidth, boardHeight));
        root.UpdateLayout();

        var board = new RenderTargetBitmap(
            (int)Math.Ceiling(boardWidth), (int)Math.Ceiling(boardHeight),
            96, 96, PixelFormats.Pbgra32);
        board.Render(root);

        const int padding = 46;
        var width = (int)boardWidth + padding * 2;
        var height = (int)boardHeight + padding * 2;

        var wallpaper = new DrawingVisual();
        using (var dc = wallpaper.RenderOpen())
        {
            var backdrop = new LinearGradientBrush(
                Color.FromRgb(27, 39, 51),
                Color.FromRgb(9, 13, 19),
                new Point(0, 0), new Point(1, 1));
            dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));

            var glow = new RadialGradientBrush(
                Color.FromArgb(150, 84, 104, 132),
                Color.FromArgb(0, 84, 104, 132));
            dc.DrawEllipse(glow, null, new Point(width * 0.2, height * 0.16), width * 0.55, height * 0.42);

            var warm = new RadialGradientBrush(
                Color.FromArgb(120, 214, 158, 84),
                Color.FromArgb(0, 214, 158, 84));
            dc.DrawEllipse(warm, null, new Point(width * 0.86, height * 0.82), width * 0.5, height * 0.42);

            dc.DrawImage(board, new Rect(padding, padding, boardWidth, boardHeight));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(wallpaper);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static AppState BuildPreviewState(string scene)
    {
        var state = new AppState();
        state.Settings.BackgroundStyle = scene.ToLowerInvariant() switch
        {
            "dots" => PanelBackgroundStyle.Dots,
            "cross" => PanelBackgroundStyle.Cross,
            "image" => PanelBackgroundStyle.Image,
            _ => PanelBackgroundStyle.Blank
        };

        if (scene.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            var imagePath = Environment.GetEnvironmentVariable("COTAB_PREVIEW_IMAGE");
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                state.Settings.BackgroundImagePath = imagePath;
                state.Settings.BackgroundImageOpacity = 1.0;
                state.Settings.PanelOpacity = 0.74;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(imagePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                var ink = ThemeColorExtractor.Extract(image, ColorUtil.Parse(state.Settings.PanelColor));
                var hex = ColorUtil.ToHex(ink);
                state.Settings.SettingsColor = hex;
                state.Settings.CloseColor = hex;
                state.Settings.AddColor = hex;
            }
        }
        else if (scene.Equals("dark", StringComparison.OrdinalIgnoreCase))
        {
            state.Settings.PanelColor = "#2C3038";
            state.Settings.PanelOpacity = 0.9;
            state.Settings.SettingsColor = "#E9DDBE";
            state.Settings.CloseColor = "#E9DDBE";
            state.Settings.AddColor = "#E9DDBE";
            state.Settings.NoteFillColor = "#F5DFA2";
            state.Settings.NoteFontColor = "#332E25";
            state.Settings.BackgroundStyle = PanelBackgroundStyle.Dots;
        }

        if (scene.Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            state.Notes.Add(new NoteModel
            {
                X = 80, Y = 180, Width = 184, Height = 184,
                Text = "默认便签\n\n- 尖角正方形\n- 单击编辑 Markdown\n- 按住拖动 / 拖到垃圾桶删除",
                Markdown = true
            });
            return state;
        }

        state.Notes.Add(new NoteModel
        {
            X = 14, Y = 54, Width = 178, Height = 140,
            Text = "买咖啡豆\n\n- 浅烘 200g\n- 周末去店里",
            Markdown = true
        });
        state.Notes.Add(new NoteModel
        {
            X = 152, Y = 126, Width = 180, Height = 172,
            Text = "### 会议 14:30\n\n- 需求评审\n- 确认交互稿\n\n`Ctrl + N` 新建便签",
            Markdown = true
        });
        state.Notes.Add(new NoteModel
        {
            X = 20, Y = 284, Width = 180, Height = 136,
            Text = "**灵感**\n\n便签可以拖到左下角垃圾桶删除。",
            Markdown = true
        });
        state.Notes.Add(new NoteModel
        {
            X = 158, Y = 342, Width = 174, Height = 150,
            Text = "待办\n\n- [x] 关闭手机通知\n- [ ] 整理桌面\n- [ ] 写周报",
            Markdown = true
        });

        return state;
    }
}
