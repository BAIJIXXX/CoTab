using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CoTab.Services;

/// <summary>
/// A deliberately small, dependency-free Markdown renderer tuned for tiny sticky notes.
/// Supports headings, paragraphs, lists, task lists, quotes, fenced code, rules and the
/// common inline styles (bold, italic, strike, code, links, escapes).
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex ListRegex = new(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskRegex = new(@"^\[([ xX])\]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex RuleRegex = new(@"^\s*((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$", RegexOptions.Compiled);

    public static StackPanel Build(string markdown, Color foreground, Color fill, double fontSize = 12.5, FontFamily? fontFamily = null)
    {
        var family = fontFamily ?? new FontFamily("Microsoft YaHei UI, Segoe UI");
        var fg = new SolidColorBrush(foreground);
        fg.Freeze();
        var muted = new SolidColorBrush(Color.FromArgb(170, foreground.R, foreground.G, foreground.B));
        muted.Freeze();
        var codeBackground = new SolidColorBrush(Color.FromArgb(26, foreground.R, foreground.G, foreground.B));
        codeBackground.Freeze();
        var ruleBrush = new SolidColorBrush(Color.FromArgb(45, foreground.R, foreground.G, foreground.B));
        ruleBrush.Freeze();

        var panel = new StackPanel { IsHitTestVisible = false };
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var i = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var text = string.Join(" ", paragraph.Select(l => l.Trim()));
            paragraph.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            var block = MakeTextBlock(family, fontSize, fg, new Thickness(0, 0, 0, 5));
            AppendInlines(block.Inlines, text, fg, muted, codeBackground, family, fontSize);
            panel.Children.Add(block);
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph();
                i++;
                continue;
            }

            if (trimmed.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                panel.Children.Add(MakeCodeBlock(code.ToString(), fg, codeBackground, family, fontSize));
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph();
                var level = heading.Groups[1].Length;
                var headingSize = level switch { 1 => fontSize + 5.5, 2 => fontSize + 3.5, 3 => fontSize + 2, _ => fontSize + 1 };
                var block = MakeTextBlock(family, headingSize, fg, new Thickness(0, level == 1 ? 2 : 5, 0, 4));
                block.FontWeight = FontWeights.SemiBold;
                AppendInlines(block.Inlines, heading.Groups[2].Value, fg, muted, codeBackground, family, headingSize);
                panel.Children.Add(block);
                i++;
                continue;
            }

            if (RuleRegex.IsMatch(trimmed))
            {
                FlushParagraph();
                var rule = new Border
                {
                    Height = 1,
                    Background = ruleBrush,
                    Margin = new Thickness(0, 3, 0, 8)
                };
                panel.Children.Add(rule);
                i++;
                continue;
            }

            if (trimmed.TrimStart().StartsWith('>'))
            {
                FlushParagraph();
                var quoteLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var content = lines[i].TrimStart();
                    content = content.Length > 1 ? content[1..] : string.Empty;
                    if (content.StartsWith(' ')) content = content[1..];
                    quoteLines.Add(content);
                    i++;
                }
                var quoteText = MakeTextBlock(family, fontSize, muted, new Thickness(8, 0, 0, 7));
                quoteText.FontStyle = FontStyles.Italic;
                AppendInlines(quoteText.Inlines, string.Join(" ", quoteLines), muted, muted, codeBackground, family, fontSize);
                var quote = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, foreground.R, foreground.G, foreground.B)),
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Child = quoteText
                };
                panel.Children.Add(quote);
                continue;
            }

            var list = ListRegex.Match(trimmed);
            if (list.Success)
            {
                FlushParagraph();
                var indent = list.Groups[1].Value.Length / 2;
                var marker = list.Groups[2].Value;
                var content = list.Groups[3].Value;
                var isOrdered = char.IsDigit(marker[0]);
                var markerText = isOrdered ? marker : "•";
                var task = TaskRegex.Match(content);
                if (task.Success)
                {
                    markerText = task.Groups[1].Value is "x" or "X" ? "☑" : "☐";
                    content = task.Groups[2].Value;
                }

                var row = new Grid { Margin = new Thickness(indent * 12, 0, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bullet = new TextBlock
                {
                    Text = markerText,
                    Foreground = muted,
                    FontFamily = family,
                    FontSize = fontSize,
                    FontWeight = task.Success ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = TextAlignment.Left,
                    LineHeight = fontSize * 1.55
                };
                if (!isOrdered && !task.Success) bullet.FontSize = fontSize + 1;
                Grid.SetColumn(bullet, 0);
                row.Children.Add(bullet);
                var textBlock = MakeTextBlock(family, fontSize, fg, new Thickness(0));
                AppendInlines(textBlock.Inlines, content, fg, muted, codeBackground, family, fontSize);
                Grid.SetColumn(textBlock, 1);
                row.Children.Add(textBlock);
                panel.Children.Add(row);
                i++;
                continue;
            }

            paragraph.Add(line);
            i++;
        }

        FlushParagraph();
        return panel;
    }

    private static TextBlock MakeTextBlock(FontFamily family, double fontSize, Brush foreground, Thickness margin)
    {
        var block = new TextBlock
        {
            FontFamily = family,
            FontSize = fontSize,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin,
            LineHeight = fontSize * 1.55,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
        return block;
    }

    private static Border MakeCodeBlock(string code, Brush foreground, Brush background, FontFamily family, double fontSize)
    {
        var text = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
            FontSize = Math.Max(10, fontSize - 1.2),
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = Math.Max(15, fontSize * 1.45),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 5, 7, 6),
            Margin = new Thickness(0, 1, 0, 7),
            Child = text
        };
    }

    private static void AppendInlines(InlineCollection target, string text, Brush foreground, Brush muted, Brush codeBackground, FontFamily family, double fontSize)
    {
        var plain = new StringBuilder();
        var i = 0;

        void Flush()
        {
            if (plain.Length == 0) return;
            target.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        while (i < text.Length)
        {
            var ch = text[i];

            if (ch == '\\' && i + 1 < text.Length)
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (ch == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    Flush();
                    var code = text[(i + 1)..end];
                    var run = new Run(code)
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
                        FontSize = Math.Max(10, fontSize - 1),
                        Background = codeBackground,
                        Foreground = foreground
                    };
                    target.Add(run);
                    i = end + 1;
                    continue;
                }
            }

            if (i + 1 < text.Length && ((ch == '*' && text[i + 1] == '*') || (ch == '_' && text[i + 1] == '_')))
            {
                var marker = text.Substring(i, 2);
                var end = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    Flush();
                    var bold = new Bold();
                    AppendInlines(bold.Inlines, text[(i + 2)..end], foreground, muted, codeBackground, family, fontSize);
                    target.Add(bold);
                    i = end + 2;
                    continue;
                }
            }

            if (ch is '*' or '_')
            {
                var end = text.IndexOf(ch, i + 1);
                if (end > i + 1)
                {
                    Flush();
                    var italic = new Italic();
                    AppendInlines(italic.Inlines, text[(i + 1)..end], foreground, muted, codeBackground, family, fontSize);
                    target.Add(italic);
                    i = end + 1;
                    continue;
                }
            }

            if (i + 1 < text.Length && ch == '~' && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    Flush();
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    AppendInlines(span.Inlines, text[(i + 2)..end], foreground, muted, codeBackground, family, fontSize);
                    target.Add(span);
                    i = end + 2;
                    continue;
                }
            }

            if (ch == '!' && i + 1 < text.Length && text[i + 1] == '[')
            {
                var linkEnd = text.IndexOf("](", i + 2, StringComparison.Ordinal);
                if (linkEnd > 0)
                {
                    var close = text.IndexOf(')', linkEnd + 2);
                    if (close > linkEnd)
                    {
                        Flush();
                        var alt = text[(i + 2)..linkEnd];
                        target.Add(new Run("🖼 " + alt) { Foreground = muted, FontStyle = FontStyles.Italic });
                        i = close + 1;
                        continue;
                    }
                }
            }

            if (ch == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i + 1 && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket)
                    {
                        Flush();
                        var label = text[(i + 1)..closeBracket];
                        var url = text[(closeBracket + 2)..closeParen];
                        var link = new Hyperlink
                        {
                            Foreground = foreground,
                            TextDecorations = TextDecorations.Underline,
                            ToolTip = url
                        };
                        AppendInlines(link.Inlines, label, foreground, muted, codeBackground, family, fontSize);
                        target.Add(link);
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            if (ch == '<' && i + 1 < text.Length)
            {
                var close = text.IndexOf('>', i + 1);
                if (close > i + 1)
                {
                    var possibleUrl = text[(i + 1)..close];
                    if (possibleUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        possibleUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush();
                        var link = new Hyperlink
                        {
                            Foreground = foreground,
                            TextDecorations = TextDecorations.Underline,
                            ToolTip = possibleUrl
                        };
                        link.Inlines.Add(new Run(possibleUrl));
                        target.Add(link);
                        i = close + 1;
                        continue;
                    }
                }
            }

            plain.Append(ch);
            i++;
        }

        Flush();
    }
}
