using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CoTab.Models;

namespace CoTab.Services;

/// <summary>
/// Small OpenAI-compatible chat client with function/tool calling.
/// No external SDK is used, so custom compatible endpoints work out of the box.
/// </summary>
public static class AiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public sealed class Message
    {
        public string Role { get; init; } = "user";
        public string? Content { get; init; }
        public List<ToolCall>? ToolCalls { get; init; }
        public string? ToolCallId { get; init; }

        public static Message User(string text) => new() { Role = "user", Content = text };
        public static Message Assistant(string text) => new() { Role = "assistant", Content = text };
        public static Message ToolResult(string callId, string content) =>
            new() { Role = "tool", ToolCallId = callId, Content = content };
    }

    public sealed class ToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Arguments { get; set; } = "{}";
    }

    public sealed class Completion
    {
        public string Content { get; set; } = string.Empty;
        public List<ToolCall> ToolCalls { get; set; } = new();
        public string? FinishReason { get; set; }
        public string Raw { get; set; } = string.Empty;
    }

    public static async Task<Completion> CompleteAsync(SettingsModel settings,
        IReadOnlyList<Message> messages, IReadOnlyList<object>? tools, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AiApiKey))
            throw new InvalidOperationException("请先在 AI 设置里填写 API Key。");
        if (string.IsNullOrWhiteSpace(settings.AiModel))
            throw new InvalidOperationException("请先在 AI 设置里填写模型名称。");

        var body = new Dictionary<string, object?>
        {
            ["model"] = settings.AiModel.Trim(),
            ["temperature"] = 0.4,
            ["messages"] = messages.Select(ToPayload).ToList()
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }

        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl(settings.AiEndpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AiApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = responseText.Length > 700 ? responseText[..700] : responseText;
            throw new InvalidOperationException($"AI 请求失败 ({(int)response.StatusCode}): {detail}");
        }

        return ParseCompletion(responseText);
    }

    public static async Task<string> TestAsync(SettingsModel settings, CancellationToken cancellationToken)
    {
        var result = await CompleteAsync(settings,
            new[] { Message.User("只回复两个字：可用") },
            tools: null, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(result.Content) ? "连接成功" : result.Content.Trim();
    }

    private static Dictionary<string, object?> ToPayload(Message message)
    {
        if (message.Role == "tool")
        {
            return new Dictionary<string, object?>
            {
                ["role"] = "tool",
                ["tool_call_id"] = message.ToolCallId,
                ["content"] = message.Content ?? string.Empty
            };
        }

        var payload = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };
        if (message.ToolCalls is { Count: > 0 })
        {
            payload["tool_calls"] = message.ToolCalls.Select(call => new Dictionary<string, object?>
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = call.Name,
                    ["arguments"] = call.Arguments
                }
            }).ToList();
        }
        return payload;
    }

    private static Completion ParseCompletion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var result = new Completion { Raw = json };
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("AI 返回内容为空或格式不兼容。");
        }

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finish))
            result.FinishReason = finish.GetString();

        if (!choice.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            return result;

        if (msg.TryGetProperty("content", out var content))
            result.Content = ReadText(content);

        if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object)
                    continue;
                result.ToolCalls.Add(new ToolCall
                {
                    Id = call.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Name = fn.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Arguments = fn.TryGetProperty("arguments", out var args) ? ReadText(args) : "{}"
                });
            }
        }

        return result;
    }

    private static string ReadText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString() ?? string.Empty;
        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    parts.Add(item.GetString() ?? string.Empty);
                }
                else if (item.ValueKind == JsonValueKind.Object &&
                         item.TryGetProperty("text", out var text))
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }
            return string.Join("\n", parts);
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var text)) return text.GetString() ?? string.Empty;
            if (element.TryGetProperty("content", out var nested)) return ReadText(nested);
        }
        return element.ToString();
    }

    private static string ChatUrl(string endpoint)
    {
        var value = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1" : endpoint.Trim().TrimEnd('/');
        if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return value;
        return value + "/chat/completions";
    }
}
