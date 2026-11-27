using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoTab.Helpers;
using CoTab.Models;
using CoTab.Services;

namespace CoTab;

public partial class AssistantWindow : Window
{
    private const double ExpandedWidth = 360;
    private const double CollapsedWidth = 60;
    private const double CollapsedHeight = 130;

    private readonly AppState _state;
    private readonly ScheduleService _schedule;
    private readonly MainWindow _main;
    private readonly AiToolService _tools;
    private readonly List<AiClient.Message> _history = new();

    private bool _busy;
    private bool _suppressTab;
    private bool _shownWelcome;

    public AssistantWindow(AppState state, ScheduleService schedule, MainWindow main)
    {
        InitializeComponent();
        _state = state;
        _schedule = schedule;
        _main = main;
        _tools = new AiToolService(main);

        BuildChips();
        ApplyTheme();
        ApplyCollapsedState();
        RefreshSettings();
        RefreshSchedule();
        Loaded += (_, _) =>
        {
            ApplyCollapsedState();
            FollowMain();
            ShowWelcome();
        };
    }

    public void ApplyTheme()
    {
        var settings = _state.Settings;
        var panel = ColorUtil.Parse(settings.PanelColor, Colors.White);
        var shell = ColorUtil.Blend(panel, Color.FromRgb(18, 19, 23), 0.90);
        ShellBorder.Background = new SolidColorBrush(Color.FromArgb(250, shell.R, shell.G, shell.B));
        ShellBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
        CollapsedTab.Background = new SolidColorBrush(Color.FromArgb(248, shell.R, shell.G, shell.B));
        CollapsedTab.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        StatusDot.Fill = HasApiKey()
            ? new SolidColorBrush(Color.FromRgb(123, 216, 143))
            : new SolidColorBrush(Color.FromRgb(139, 144, 154));
        UpdateStatusCaption();
    }

    public void FollowMain()
    {
        if (!_state.Settings.AiEnabled || _main.IsHiddenToTrayForAi || _main.IsCollapsedForAi || !_main.IsVisible)
        {
            if (IsVisible) Hide();
            return;
        }

        var work = ScreenUtil.GetWorkAreaDip(_main);
        var collapsed = _state.Settings.AiCollapsed;
        var width = collapsed ? CollapsedWidth : ExpandedWidth;
        var height = collapsed ? CollapsedHeight : Math.Clamp(_main.Height, 320, Math.Max(320, work.Height - 16));

        if (Math.Abs(Width - width) > 0.1) Width = width;
        if (Math.Abs(Height - height) > 0.1) Height = height;

        const double gap = 8;
        var left = _main.Left + _main.Width + gap;
        if (left + width > work.Right - 4)
            left = _main.Left - gap - width;
        left = Math.Clamp(left, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));

        var top = collapsed
            ? _main.Top + _main.Height / 2 - height / 2
            : _main.Top;
        top = Math.Clamp(top, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));

        Left = left;
        Top = top;
        Topmost = _main.Topmost;
    }

    public void ShowSettingsTab()
    {
        RefreshSettings();
        _suppressTab = true;
        SettingsTab.IsChecked = true;
        _suppressTab = false;
        ShowView(2);
    }

    public void ShowChatTab() => SelectTab(ChatTab, 0);

    public void ShowScheduleTab() => SelectTab(ScheduleTab, 1);

    public void SetCollapsed(bool collapsed)
    {
        _state.Settings.AiCollapsed = collapsed;
        ApplyCollapsedState();
        FollowMain();
    }

    private void SelectTab(RadioButton tab, int index)
    {
        _suppressTab = true;
        tab.IsChecked = true;
        _suppressTab = false;
        ShowView(index);
    }

    public void RefreshSchedule()
    {
        if (ScheduleListPanel == null) return;
        ScheduleListPanel.Children.Clear();
        var events = _schedule.All;
        if (events.Count == 0)
        {
            ScheduleListPanel.Children.Add(new TextBlock
            {
                Text = "还没有识别到日程。\n在便签里写「明天 14:30 开会」这样的内容试试。",
                Foreground = Brush("#7F8692"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 18, 0, 0)
            });
            return;
        }

        foreach (var evt in events)
        {
            var card = new Border
            {
                Background = Brush("#10FFFFFF"),
                BorderBrush = Brush("#18FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 7)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = evt.Title,
                Foreground = Brush("#F1F2F5"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{ScheduleService.FormatTime(evt)} · 提前 {evt.ReminderMinutes} 分钟",
                Foreground = Brush("#8E96A2"),
                FontSize = 10.8,
                Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = stack;
            ScheduleListPanel.Children.Add(card);
        }
    }

    public void AddSuggestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AddMessage("suggestion", text);
        _history.Add(AiClient.Message.Assistant(text));
        if (!IsVisible) StatusDot.Fill = Brush("#FFD66B");
    }

    private void ShowWelcome()
    {
        if (_shownWelcome) return;
        _shownWelcome = true;
        if (_history.Count > 0) return;
        AddMessage("assistant",
            "我是 CoTab 的 AI 助手。可以让我帮你整理便签、按分类创建板块，或者从便签里识别日程。\n\n默认静默，只在你提问时工作。", false);
    }

    private void BuildChips()
    {
        AddChip("整理日程", "帮我查看当前所有日程，按时间顺序整理，并指出可能的冲突。");
        AddChip("拆成便签", "请把下面这段话拆成几张简洁的便签：\n");
        AddChip("创建板块", "请根据我现有的便签，建议并创建几个合适的分类板块。");
        AddChip("今日规划", "根据我的日程和便签，帮我规划今天剩下的时间。");
        AddChip("检查冲突", "检查我的日程是否存在时间冲突，并给出调整建议。");
    }

    private void AddChip(string label, string prompt)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("AiChip")
        };
        button.Click += (_, _) =>
        {
            InputBox.Text = prompt;
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
        };
        ChipsPanel.Children.Add(button);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressTab || sender is not RadioButton radio) return;
        if (!int.TryParse(radio.Tag?.ToString(), out var index)) return;
        ShowView(index);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (InputPlaceholder != null)
            InputPlaceholder.Visibility = string.IsNullOrEmpty(InputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ShowView(int index)
    {
        if (ChatView == null) return;
        ChatView.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduleView.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 1) RefreshSchedule();
        if (index == 2) RefreshSettings();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift) return;
        e.Handled = true;
        await SendAsync();
    }

    private async Task SendAsync()
    {
        if (_busy) return;
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;

        if (!HasApiKey())
        {
            ShowSettingsTab();
            AddMessage("assistant", "请先在「设置」页填写 API 地址、API Key 和模型名称。", false);
            return;
        }

        InputBox.Clear();
        AddMessage("user", text);
        _history.Add(AiClient.Message.User(text));
        SetBusy(true);

        try
        {
            var tools = _tools.Definitions;
            for (var round = 0; round < 4; round++)
            {
                StatusText.Text = "思考中…";
                var completion = await AiClient.CompleteAsync(_state.Settings, BuildMessages(), tools, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(completion.Content))
                    AddMessage("assistant", completion.Content);

                _history.Add(new AiClient.Message
                {
                    Role = "assistant",
                    Content = completion.Content,
                    ToolCalls = completion.ToolCalls.Count > 0 ? completion.ToolCalls : null
                });

                if (completion.ToolCalls.Count == 0) break;

                foreach (var call in completion.ToolCalls)
                {
                    StatusText.Text = $"正在执行 {call.Name}…";
                    var result = _tools.Execute(call.Name, call.Arguments);
                    _history.Add(AiClient.Message.ToolResult(call.Id, result));
                    AddToolTrace(call.Name, result);
                }
            }
        }
        catch (Exception ex)
        {
            AddMessage("assistant", "出错了：" + ex.Message, false);
        }
        finally
        {
            SetBusy(false);
            TrimHistory();
        }
    }

    private IReadOnlyList<AiClient.Message> BuildMessages()
    {
        var messages = new List<AiClient.Message>
        {
            new() { Role = "system", Content = BuildSystemPrompt() }
        };
        messages.AddRange(_history.TakeLast(26));
        return messages;
    }

    private string BuildSystemPrompt()
    {
        var s = _state.Settings;
        var boardLines = _state.Boards.Take(12).Select(b => $"- {b.Title}（{b.Notes.Count} 张便签，id {b.Id}）");
        var scheduleLines = _schedule.All.Take(8).Select(e => $"- {e.StartAt:yyyy-MM-dd HH:mm} {e.Title}");
        var builder = new StringBuilder();
        builder.AppendLine("你是 CoTab 的桌面便签 AI 助手，运行在一个可选的小窗口中。");
        builder.AppendLine($"当前时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm}（{TimeZoneInfo.Local.DisplayName}）。");
        builder.AppendLine($"当前模式：{(_main.CurrentMode == AppMode.MultiBoard ? "多板模式" : "极简模式")}。");
        if (_main.CurrentMode == AppMode.MultiBoard)
        {
            builder.AppendLine("现有板块：");
            builder.AppendLine(boardLines.Any() ? string.Join("\n", boardLines) : "- 暂无板块");
            builder.AppendLine("你可以调用 create_board 创建板块，调用 create_note 在指定 board_id 或当前板块创建便签。");
        }
        else
        {
            builder.AppendLine("极简模式没有子板，仅可使用 create_note 创建便签。");
        }

        if (scheduleLines.Any())
        {
            builder.AppendLine("已识别日程：");
            builder.AppendLine(string.Join("\n", scheduleLines));
        }

        builder.AppendLine("工具使用规则：创建板块或便签前先调用 get_state；用户没有明确要求修改时不要调用写工具；一次最多执行 4 轮工具。");
        builder.AppendLine("回答用简体中文，简短、直接。不要声称自己能做到工具之外的事情。");
        if (!string.IsNullOrWhiteSpace(s.AiSystemPrompt))
        {
            builder.AppendLine("用户自定义提示：");
            builder.AppendLine(s.AiSystemPrompt.Trim());
        }
        return builder.ToString();
    }

    private void AddToolTrace(string name, string result)
    {
        var label = name switch
        {
            "create_board" => "已创建板块",
            "create_note" => "已创建便签",
            "open_board" => "已打开板块",
            "list_schedule" => "已读取日程",
            "get_state" or "list_boards" => "已读取便签状态",
            _ => "工具调用完成"
        };
        AddMessage("tool", label, false);
    }

    private void AddMessage(string role, string text, bool markdown = true)
    {
        var isUser = role == "user";
        var isTool = role == "tool";
        var isSuggestion = role == "suggestion";

        var bubble = new Border
        {
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = isTool ? 250 : 286,
            CornerRadius = new CornerRadius(8),
            Padding = isTool ? new Thickness(8, 4, 8, 4) : new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 7),
            Background = isUser ? Brush("#FFD66B")
                : isSuggestion ? Brush("#16FFD66B")
                : isTool ? Brush("#0CFFFFFF")
                : Brush("#18FFFFFF"),
            BorderBrush = isSuggestion ? Brush("#55FFD66B") : Brush("#12FFFFFF"),
            BorderThickness = new Thickness(1)
        };

        UIElement content;
        if (markdown && !isUser && !isTool)
        {
            content = MarkdownRenderer.Build(text, isSuggestion ? Color.FromRgb(255, 224, 150) : Color.FromRgb(241, 242, 245),
                isSuggestion ? Color.FromRgb(43, 37, 24) : Color.FromRgb(28, 29, 34), 12, FontFamily);
        }
        else
        {
            content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = isTool ? 10.8 : 12,
                Foreground = isUser ? Brush("#322B18")
                    : isTool ? Brush("#7F8692")
                    : isSuggestion ? Brush("#FFE096")
                    : Brush("#F1F2F5")
            };
        }

        bubble.Child = content;
        ChatPanel.Children.Add(bubble);
        if (isSuggestion)
        {
            var tag = new TextBlock
            {
                Text = "建议 · 静默生成",
                Foreground = Brush("#7F8692"),
                FontSize = 9.8,
                Margin = new Thickness(2, -4, 0, 10)
            };
            ChatPanel.Children.Add(tag);
        }

        Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendButton.IsEnabled = !busy;
        InputBox.IsEnabled = !busy;
        SendButton.Content = busy ? "…" : "发送";
        StatusText.Text = busy ? "调用模型中…" : "静默待命 · 只在提问时调用模型";
    }

    private void TrimHistory()
    {
        if (_history.Count <= 40) return;
        _history.RemoveRange(0, _history.Count - 40);
    }

    private void RefreshSettings()
    {
        EndpointBox.Text = _state.Settings.AiEndpoint;
        ApiKeyBox.Password = _state.Settings.AiApiKey;
        ModelBox.Text = _state.Settings.AiModel;
        SystemPromptBox.Text = _state.Settings.AiSystemPrompt;
        AutoScheduleSwitch.IsChecked = _state.Settings.AiAutoSchedule;
        AutoSuggestionSwitch.IsChecked = _state.Settings.AiAutoSuggestions;
        ReminderBox.Text = _state.Settings.AiReminderMinutes.ToString();
        SettingsStatusText.Text = HasApiKey() ? "已配置。保存后立即生效。" : "尚未配置 API Key。";
        UpdateStatusCaption();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFields();
        _main.PersistAiSettings();
        _main.RescanSchedule();
        SettingsStatusText.Text = "已保存到本机。";
        UpdateStatusCaption();
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (TestButtonIsBusy()) return;
        SaveSettingsFields();
        if (!HasApiKey())
        {
            SettingsStatusText.Text = "请先填写 API Key 和模型。";
            return;
        }

        SettingsStatusText.Text = "正在测试连接…";
        try
        {
            var reply = await AiClient.TestAsync(_state.Settings, CancellationToken.None);
            SettingsStatusText.Text = "连接成功：" + reply;
            StatusDot.Fill = Brush("#7BD88F");
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "连接失败：" + ex.Message;
            StatusDot.Fill = Brush("#E8544A");
        }
    }

    private bool TestButtonIsBusy() => string.Equals(SettingsStatusText.Text, "正在测试连接…", StringComparison.Ordinal);

    private void SaveSettingsFields()
    {
        var s = _state.Settings;
        s.AiEndpoint = string.IsNullOrWhiteSpace(EndpointBox.Text) ? "https://api.openai.com/v1" : EndpointBox.Text.Trim();
        s.AiApiKey = ApiKeyBox.Password.Trim();
        s.AiModel = string.IsNullOrWhiteSpace(ModelBox.Text) ? "gpt-4o-mini" : ModelBox.Text.Trim();
        s.AiSystemPrompt = SystemPromptBox.Text.Trim();
        s.AiAutoSchedule = AutoScheduleSwitch.IsChecked == true;
        s.AiAutoSuggestions = AutoSuggestionSwitch.IsChecked == true;
        s.AiReminderMinutes = int.TryParse(ReminderBox.Text, out var minutes) ? Math.Clamp(minutes, 1, 120) : 15;
        ReminderBox.Text = s.AiReminderMinutes.ToString();
        RefreshSettings();
    }

    private void RefreshScheduleButton_Click(object sender, RoutedEventArgs e) => RefreshSchedule();

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _state.Settings.AiCollapsed = true;
        _main.PersistAiSettings();
        ApplyCollapsedState();
        FollowMain();
    }

    private void CollapsedTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _state.Settings.AiCollapsed = false;
        _main.PersistAiSettings();
        ApplyCollapsedState();
        FollowMain();
        Activate();
    }

    private void ApplyCollapsedState()
    {
        var collapsed = _state.Settings.AiCollapsed;
        ShellBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedTab.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        Width = collapsed ? CollapsedWidth : ExpandedWidth;
        Height = collapsed ? CollapsedHeight : Math.Max(320, _main.Height);
        if (collapsed && IsVisible)
            FollowMain();
    }

    private bool HasApiKey() =>
        !string.IsNullOrWhiteSpace(_state.Settings.AiApiKey) &&
        !string.IsNullOrWhiteSpace(_state.Settings.AiModel);

    private void UpdateStatusCaption()
    {
        if (_busy) return;
        StatusText.Text = HasApiKey()
            ? "静默待命 · 只在提问时调用模型"
            : "尚未配置 API · 请打开设置页";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyCollapsedState();
        FollowMain();
        ShowWelcome();
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
