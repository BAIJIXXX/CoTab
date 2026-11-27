using System.Text.Json;
using System.Windows;

namespace CoTab.Services;

/// <summary>
/// MCP-style tool bridge exposed to the model. The JSON schemas are deliberately
/// transport-neutral so the same definitions can later be served by a real
/// stdio/HTTP MCP host without changing the UI layer.
/// </summary>
public sealed class AiToolService
{
    private readonly MainWindow _main;

    public AiToolService(MainWindow main) => _main = main;

    public IReadOnlyList<object> Definitions { get; } = BuildDefinitions();

    public string Execute(string name, string arguments)
    {
        try
        {
            using var doc = string.IsNullOrWhiteSpace(arguments) ? null : JsonDocument.Parse(arguments);
            var root = doc?.RootElement;
            string Text(string key) => root is { } r && r.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
            bool Flag(string key, bool fallback) => root is { } r && r.TryGetProperty(key, out var value)
                ? value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b) && b)
                : fallback;
            bool? OptionalFlag(string key) => root is { } r && r.TryGetProperty(key, out var value)
                ? value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b) && b)
                : null;

            return RunOnUi(() => name switch
            {
                "get_state" => _main.AiGetStateJson(),
                "list_boards" => _main.AiGetStateJson(),
                "list_notes" => _main.AiListNotes(Text("board_id")),
                "list_schedule" => _main.AiListSchedule(),
                "create_board" => _main.AiCreateBoard(Text("title"), Text("color")),
                "create_note" => _main.AiCreateNote(Text("content"), Text("board_id"), Flag("markdown", true)),
                "update_note" => _main.AiUpdateNote(Text("note_id"), Text("content"), OptionalFlag("markdown"), Text("board_id")),
                "open_board" => _main.AiOpenBoard(Text("board_id")),
                _ => JsonSerializer.Serialize(new { ok = false, error = $"未知工具: {name}" })
            });
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "工具参数不是合法 JSON。" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }
    }

    private static string RunOnUi(Func<string> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) return action();
        return dispatcher.Invoke(action);
    }

    private static List<object> BuildDefinitions()
    {
        static object Tool(string name, string description, object properties, string[]? required = null) => new
        {
            type = "function",
            function = new
            {
                name,
                description,
                parameters = new
                {
                    type = "object",
                    properties,
                    required = required ?? Array.Empty<string>()
                }
            }
        };

        var noProps = new Dictionary<string, object>();

        return new List<object>
        {
            Tool("get_state",
                "读取 CoTab 当前状态：模式、板块、便签摘要和日程。执行任何创建操作前先调用它。",
                noProps),
            Tool("list_boards",
                "列出多板模式下的所有板块及其便签数量。",
                noProps),
            Tool("create_board",
                "在多板模式下创建一个新的分类板块。极简模式下会返回错误。",
                new Dictionary<string, object>
                {
                    ["title"] = new { type = "string", description = "板块名称，最多 30 字" },
                    ["color"] = new { type = "string", description = "可选的 #RRGGBB 色值；省略时使用默认的深色板块色" }
                },
                new[] { "title" }),
            Tool("create_note",
                "在极简模式的便签板，或多板模式指定/当前板块中创建一张便签。内容支持 Markdown。",
                new Dictionary<string, object>
                {
                    ["content"] = new { type = "string", description = "便签正文，支持 Markdown" },
                    ["board_id"] = new { type = "string", description = "可选，板块 id 或板块标题；多板模式下省略时使用当前板块" },
                    ["markdown"] = new { type = "boolean", description = "是否按 Markdown 渲染，默认 true" }
                },
                new[] { "content" }),
            Tool("list_notes",
                "读取便签全文，用于在修改内容前找到准确的 note_id。可按 board_id/板块标题过滤；多板模式不传时返回所有板块的便签。",
                new Dictionary<string, object>
                {
                    ["board_id"] = new { type = "string", description = "可选，板块 id 或标题" }
                }),
            Tool("update_note",
                "修改一张已有便签的正文（支持 Markdown）。改完后 CoTab 会自动重新识别便签中的时间安排并更新日程提醒。修改日程应通过修改便签正文完成。",
                new Dictionary<string, object>
                {
                    ["note_id"] = new { type = "string", description = "要修改的便签 id，先用 list_notes/get_state 获取" },
                    ["content"] = new { type = "string", description = "新的便签正文；不要只写时间，保留完整内容" },
                    ["markdown"] = new { type = "boolean", description = "可选，是否按 Markdown 渲染；省略时保持原设置" },
                    ["board_id"] = new { type = "string", description = "可选，多板模式下便签所属板块；不确定时可省略，会自动搜索" }
                },
                new[] { "note_id", "content" }),
            Tool("open_board",
                "打开一个子板，让用户直接看到它。",
                new Dictionary<string, object>
                {
                    ["board_id"] = new { type = "string", description = "板块 id 或标题" }
                },
                new[] { "board_id" }),
            Tool("list_schedule",
                "读取自动识别出的日程事件。",
                noProps)
        };
    }
}
