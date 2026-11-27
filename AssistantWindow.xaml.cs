using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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

    private Color _panelColor = Colors.White;
    private Color _textColor = Color.FromRgb(36, 37, 42);
    private Color _mutedColor = Color.FromRgb(109, 116, 128);
    private Color _cardColor = Color.FromArgb(14, 0, 0, 0);
    private Color _borderColor = Color.FromArgb(34, 0, 0, 0);
    private Color _accentColor = Color.FromRgb(255, 214, 107);
    private Color _accentTextColor = Color.FromRgb(36, 37, 42);
    private Color _codeFillColor = Color.FromRgb(238, 238, 238);
    private SolidColorBrush _textBrush = new(Color.FromRgb(36, 37, 42));
    private SolidColorBrush _mutedBrush = new(Color.FromRgb(109, 116, 128));
    private SolidColorBrush _cardBrush = new(Color.FromArgb(14, 0, 0, 0));
    private SolidColorBrush _borderBrush = new(Color.FromArgb(34, 0, 0, 0));
    private SolidColorBrush _accentBrush = new(Color.FromRgb(255, 214, 107));

    public bool IsOnRightSide { get; private set; } = true;

    public AssistantWindow(AppState state, ScheduleService schedule, MainWindow main)
    {
        InitializeComponent();
        _state = state;
        _schedule = schedule;
        _main = main;
        _tools = new AiToolService(main);
        AvatarImage.Source = LoadAvatar();

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
        AssistantNameText.Text = AssistantName;
        Title = AssistantName;
        _panelColor = ColorUtil.Parse(settings.PanelColor, Colors.White);
        _accentColor = ColorUtil.Parse(settings.AiAccentColor, Color.FromRgb(255, 214, 107));

        var dark = ColorUtil.Luminance(_panelColor) < 0.46;
        _textColor = dark ? Color.FromRgb(244, 245, 248) : Color.FromRgb(36, 37, 42);
        _mutedColor = dark ? Color.FromRgb(157, 164, 176) : Color.FromRgb(104, 111, 124);
        _cardColor = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(13, 0, 0, 0);
        _borderColor = dark ? Color.FromArgb(44, 255, 255, 255) : Color.FromArgb(32, 0, 0, 0);
        _accentTextColor = ColorUtil.Luminance(_accentColor) > 0.55
            ? Color.FromRgb(32, 33, 38)
            : Colors.White;
        _codeFillColor = ColorUtil.Blend(_panelColor, _textColor, 0.09);

        var shell = ColorUtil.WithAlpha(_panelColor, Math.Clamp(settings.PanelOpacity, 0.18, 1.0));
        var hover = dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
        var segment = dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(10, 0, 0, 0);
        var thumb = dark ? Color.FromArgb(84, 255, 255, 255) : Color.FromArgb(54, 0, 0, 0);
        var thumbHover = dark ? Color.FromArgb(118, 255, 255, 255) : Color.FromArgb(82, 0, 0, 0);

        Resources["AiShellBrush"] = new SolidColorBrush(shell);
        Resources["AiTextBrush"] = _textBrush = new SolidColorBrush(_textColor);
        Resources["AiMutedBrush"] = _mutedBrush = new SolidColorBrush(_mutedColor);
        Resources["AiCardBrush"] = _cardBrush = new SolidColorBrush(_cardColor);
        Resources["AiBorderBrush"] = _borderBrush = new SolidColorBrush(_borderColor);
        Resources["AiHoverBrush"] = new SolidColorBrush(hover);
        Resources["AiSegmentBrush"] = new SolidColorBrush(segment);
        Resources["AiAccentBrush"] = _accentBrush = new SolidColorBrush(_accentColor);
        Resources["AiAccentTextBrush"] = new SolidColorBrush(_accentTextColor);
        Resources["AiScrollThumbBrush"] = new SolidColorBrush(thumb);
        Resources["AiScrollThumbHoverBrush"] = new SolidColorBrush(thumbHover);
        Resources["AccentBrush"] = new SolidColorBrush(_accentColor);
        Resources["IconHoverBrush"] = new SolidColorBrush(hover);
        Resources["IconPressedBrush"] = new SolidColorBrush(dark
            ? Color.FromArgb(44, 255, 255, 255)
            : Color.FromArgb(28, 0, 0, 0));

        AccentSwatch.Background = _accentBrush;
        AccentHexText.Text = ColorUtil.ToHex(_accentColor).ToUpperInvariant();
        UpdateStatusCaption();
        if (ScheduleListPanel != null) RefreshSchedule();
    }

    public void FollowMain()
    {
        var work = ScreenUtil.GetWorkAreaDip(_main);
        const double panelInset = 16;
        const double shellMargin = 10;
        const double panelGap = 4;
        var panelLeft = _main.Left + panelInset;
        var panelRight = _main.Left + _main.Width - panelInset;
        var rightShellLeft = panelRight + panelGap - shellMargin;
        IsOnRightSide = rightShellLeft + ExpandedWidth <= work.Right - 4;

        if (!_state.Settings.AiEnabled || _state.Settings.AiCollapsed ||
            _main.IsHiddenToTrayForAi || _main.IsCollapsedForAi || !_main.IsVisible)
        {
            if (IsVisible) Hide();
            return;
        }

        var width = ExpandedWidth;

        // The main panel is inset 16 DIP inside its window; align the assistant
        // shell to the visible panel, not to the transparent shadow margin.
        var panelTop = _main.Top + panelInset;
        var panelHeight = Math.Max(320, _main.Height - panelInset * 2);
        var height = Math.Clamp(panelHeight + shellMargin * 2, 320, Math.Max(320, work.Height - 8));

        if (Math.Abs(Width - width) > 0.1) Width = width;
        if (Math.Abs(Height - height) > 0.1) Height = height;

        var left = IsOnRightSide
            ? rightShellLeft
            : panelLeft - panelGap - width + shellMargin;
        left = Math.Clamp(left, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));

        var top = panelTop - shellMargin;
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
        if (collapsed)
        {
            if (IsVisible) Hide();
        }
        else
        {
            if (!IsVisible) Show();
            FollowMain();
        }
        _main.UpdateAiPageHint();
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
                Foreground = _mutedBrush,
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
                Background = _cardBrush,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 7)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });

            var bar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(ScheduleUrgencyColor(evt.StartAt)),
                Margin = new Thickness(0, 1, 8, 1),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);

            var eventCell = new StackPanel
            {
                Margin = new Thickness(0, 1, 9, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            eventCell.Children.Add(new TextBlock
            {
                Text = evt.Title,
                Foreground = _textBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            eventCell.Children.Add(new TextBlock
            {
                Text = $"提前 {evt.ReminderMinutes} 分钟",
                Foreground = _mutedBrush,
                FontSize = 10.2,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(eventCell, 1);
            grid.Children.Add(eventCell);

            var timeCell = new Border
            {
                Background = _accentBrush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 7, 5),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var timeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            timeStack.Children.Add(new TextBlock
            {
                Text = ScheduleService.FormatDate(evt),
                Foreground = new SolidColorBrush(ColorUtil.WithAlpha(_accentTextColor, 0.74)),
                FontSize = 10,
                TextWrapping = TextWrapping.NoWrap
            });
            timeStack.Children.Add(new TextBlock
            {
                Text = $"{ScheduleService.FormatWeekday(evt)} {ScheduleService.FormatClock(evt)}",
                Foreground = new SolidColorBrush(_accentTextColor),
                FontSize = evt.HasTime ? 12.5 : 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.NoWrap
            });
            timeCell.Child = timeStack;
            Grid.SetColumn(timeCell, 2);
            grid.Children.Add(timeCell);

            card.Child = grid;
            ScheduleListPanel.Children.Add(card);
        }
    }

    private Color ScheduleUrgencyColor(DateTimeOffset start)
    {
        var days = (start.Date - DateTimeOffset.Now.Date).Days;
        var text = days <= 3
            ? _state.Settings.AiScheduleSoonColor
            : days <= 7
                ? _state.Settings.AiScheduleWeekColor
                : _state.Settings.AiScheduleLaterColor;
        var fallback = days <= 3
            ? Color.FromRgb(176, 122, 122)
            : days <= 7
                ? Color.FromRgb(194, 168, 120)
                : Color.FromRgb(140, 158, 134);
        return ColorUtil.Parse(text, fallback);
    }

    public void AddSuggestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AddMessage("suggestion", text);
        _history.Add(AiClient.Message.Assistant(text));
    }

    private void ShowWelcome()
    {
        if (_shownWelcome) return;
        _shownWelcome = true;
        if (_history.Count > 0) return;
        AddMessage("assistant",
            $"我是 {AssistantName}。可以让我帮你整理便签、按分类创建板块，或者修改便签里的时间安排并同步日程。\n\n默认静默，只在你提问时工作。", false);
    }

    private void BuildChips()
    {
        AddChip("整理日程", "帮我查看当前所有日程，按时间顺序整理，并指出可能的冲突。");
        AddChip("拆成便签", "请把下面这段话拆成几张简洁的便签：\n");
        AddChip("创建板块", "请根据我现有的便签，建议并创建几个合适的分类板块。");
        AddChip("今日规划", "根据我的日程和便签，帮我规划今天剩下的时间。");
        AddChip("检查冲突", "检查我的日程是否存在时间冲突，并给出调整建议。");
        AddChip("改便签日程", "请先列出我有日程的便签和当前识别出的时间；我想修改便签里的时间安排，等我确认具体便签后再用 update_note 改写。");
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
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers == ModifierKeys.Shift) return;

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
        builder.AppendLine($"你是 {AssistantName}，CoTab 的桌面便签助手，运行在一个可选的小窗口中。");
        builder.AppendLine($"当前时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm}（{TimeZoneInfo.Local.DisplayName}）。");
        builder.AppendLine($"当前模式：{(_main.CurrentMode == AppMode.MultiBoard ? "多板模式" : "极简模式")}。");
        if (_main.CurrentMode == AppMode.MultiBoard)
        {
            builder.AppendLine("现有板块：");
            builder.AppendLine(boardLines.Any() ? string.Join("\n", boardLines) : "- 暂无板块");
            builder.AppendLine("你可以调用 create_board 创建板块，调用 create_note 在指定 board_id 或当前板块创建便签，调用 update_note 修改已有便签。");
        }
        else
        {
            builder.AppendLine("极简模式没有子板，可使用 create_note 创建便签、update_note 修改便签。");
        }

        if (scheduleLines.Any())
        {
            builder.AppendLine("已识别日程：");
            builder.AppendLine(string.Join("\n", scheduleLines));
        }

        builder.AppendLine("工具使用规则：创建板块或便签前先调用 get_state；修改便签或日程前先调用 list_notes 找到 note_id，再用 update_note 改写完整正文；用户没有明确要求修改时不要调用写工具；一次最多执行 4 轮工具。");
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
            "update_note" => "已修改便签",
            "open_board" => "已打开板块",
            "list_notes" => "已读取便签内容",
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
            Background = isUser ? _accentBrush
                : isSuggestion ? new SolidColorBrush(ColorUtil.WithAlpha(_accentColor, 0.16))
                : isTool ? new SolidColorBrush(ColorUtil.WithAlpha(_mutedColor, 0.12))
                : _cardBrush,
            BorderBrush = isSuggestion
                ? new SolidColorBrush(ColorUtil.WithAlpha(_accentColor, 0.44))
                : _borderBrush,
            BorderThickness = new Thickness(1)
        };

        UIElement content;
        if (markdown && !isUser && !isTool)
        {
            content = MarkdownRenderer.Build(text, _textColor, _codeFillColor, 12, FontFamily);
        }
        else
        {
            content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = isTool ? 10.8 : 12,
                Foreground = isUser ? new SolidColorBrush(_accentTextColor)
                    : isTool ? _mutedBrush
                    : _textBrush
            };
        }

        bubble.Child = content;
        ChatPanel.Children.Add(bubble);
        if (isSuggestion)
        {
            var tag = new TextBlock
            {
                Text = "建议 · 静默生成",
                Foreground = _mutedBrush,
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

    private string AssistantName
    {
        get
        {
            var value = _state.Settings.AiAssistantName;
            return string.IsNullOrWhiteSpace(value) ? "cochubbylung" : value.Trim();
        }
    }

    private void RefreshSettings()
    {
        AssistantNameBox.Text = AssistantName;
        EndpointBox.Text = _state.Settings.AiEndpoint;
        ApiKeyBox.Password = _state.Settings.AiApiKey;
        ModelBox.Text = _state.Settings.AiModel;
        SystemPromptBox.Text = _state.Settings.AiSystemPrompt;
        AutoScheduleSwitch.IsChecked = _state.Settings.AiAutoSchedule;
        AutoSuggestionSwitch.IsChecked = _state.Settings.AiAutoSuggestions;
        ReminderBox.Text = _state.Settings.AiReminderMinutes.ToString();
        var accent = ColorUtil.Parse(_state.Settings.AiAccentColor, Color.FromRgb(255, 214, 107));
        AccentSwatch.Background = new SolidColorBrush(accent);
        AccentHexText.Text = ColorUtil.ToHex(accent).ToUpperInvariant();
        SoonSwatch.Background = new SolidColorBrush(ColorUtil.Parse(_state.Settings.AiScheduleSoonColor, Color.FromRgb(176, 122, 122)));
        WeekSwatch.Background = new SolidColorBrush(ColorUtil.Parse(_state.Settings.AiScheduleWeekColor, Color.FromRgb(194, 168, 120)));
        LaterSwatch.Background = new SolidColorBrush(ColorUtil.Parse(_state.Settings.AiScheduleLaterColor, Color.FromRgb(140, 158, 134)));
        SettingsStatusText.Text = HasApiKey() ? "已配置。保存后立即生效。" : "尚未配置 API Key。";
        UpdateStatusCaption();
    }

    private void AssistantNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var raw = AssistantNameBox.Text?.Trim();
        var name = string.IsNullOrWhiteSpace(raw) ? "cochubbylung" : raw;
        _state.Settings.AiAssistantName = name;
        AssistantNameText.Text = name;
        Title = name;
    }

    private void AssistantNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        RefreshSettings();
        _main.PersistAiSettings();
    }

    private void AccentPicker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var picker = new ColorPickerWindow(ColorUtil.Parse(_state.Settings.AiAccentColor, Color.FromRgb(255, 214, 107)))
        {
            Owner = this
        };
        if (picker.ShowDialog() != true) return;
        _state.Settings.AiAccentColor = ColorUtil.ToHex(picker.SelectedColor);
        _main.PersistAiSettings();
        ApplyTheme();
        RefreshSettings();
        SettingsStatusText.Text = "主题色已更新。";
    }

    private void ScheduleColorPicker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var settings = _state.Settings;
        var currentText = tag switch
        {
            "soon" => settings.AiScheduleSoonColor,
            "week" => settings.AiScheduleWeekColor,
            _ => settings.AiScheduleLaterColor
        };
        var fallback = tag switch
        {
            "soon" => Color.FromRgb(176, 122, 122),
            "week" => Color.FromRgb(194, 168, 120),
            _ => Color.FromRgb(140, 158, 134)
        };
        var picker = new ColorPickerWindow(ColorUtil.Parse(currentText, fallback)) { Owner = this };
        if (picker.ShowDialog() != true) return;

        var hex = ColorUtil.ToHex(picker.SelectedColor);
        switch (tag)
        {
            case "soon": settings.AiScheduleSoonColor = hex; break;
            case "week": settings.AiScheduleWeekColor = hex; break;
            default: settings.AiScheduleLaterColor = hex; break;
        }
        _main.PersistAiSettings();
        RefreshSettings();
        RefreshSchedule();
        SettingsStatusText.Text = "日程颜色已更新。";
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
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = "连接失败：" + ex.Message;
        }
    }

    private bool TestButtonIsBusy() => string.Equals(SettingsStatusText.Text, "正在测试连接…", StringComparison.Ordinal);

    private void SaveSettingsFields()
    {
        var s = _state.Settings;
        s.AiAssistantName = string.IsNullOrWhiteSpace(AssistantNameBox.Text)
            ? "cochubbylung"
            : AssistantNameBox.Text.Trim();
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
        Hide();
        _main.UpdateAiPageHint();
    }

    private void CollapsedTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetCollapsed(false);
        Activate();
    }

    public void ApplyCollapsedState()
    {
        var collapsed = _state.Settings.AiCollapsed;
        ShellBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        // The old narrow side tab is replaced by the page edge rendered on the
        // main window itself, so the assistant window simply hides.
        CollapsedTab.Visibility = Visibility.Collapsed;
        Width = ExpandedWidth;
        Height = Math.Max(320, _main.Height);
        if (collapsed && IsVisible)
            Hide();
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

    private static ImageSource? LoadAvatar()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CoTab.AssistantAvatar.png");
        if (stream == null) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
