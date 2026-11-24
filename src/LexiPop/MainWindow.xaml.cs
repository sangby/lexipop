using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LexiPop.Interop;
using LexiPop.Models;
using LexiPop.Services;

namespace LexiPop;

public partial class MainWindow : Window
{
    private bool _suppressHide;
    private DateTime _lastShownUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        ApplyWindowMaterial(hwnd);

        var corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        ThemeService.Changed += OnThemeChanged;
    }

    /// <summary>主题切换：重新应用窗口材质，保证玻璃主题与普通主题互不残留。</summary>
    private void OnThemeChanged()
    {
        ApplyWindowMaterial(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// glass 主题：用 SetWindowCompositionAttribute 开启 Acrylic（真实 backdrop blur），
    /// 窗口背景设为透明让模糊透出；失败则回退不透明深色底，避免黑屏。
    /// 其余主题：关闭 Acrylic，使用不透明主题底色。
    /// </summary>
    private void ApplyWindowMaterial(IntPtr hwnd)
    {
        // 窗口透明与面板底色由 XAML 的 AllowsTransparency + ThemeWindowSurface 控制；
        // 这里额外尝试 Acrylic 作为增强（WPF 上不一定生效，失败也不影响通透感）。
        if (hwnd == IntPtr.Zero) return;

        if (ThemeService.Current.IsGlass)
        {
            WindowEffects.TryEnableAcrylic(hwnd, 0x80FFFFFF);
        }
        else
        {
            WindowEffects.Disable(hwnd);
        }
    }

    /// <summary>
    /// 启动时显示窗口，2.5 秒后若用户还没输入就自动隐藏。
    /// </summary>
    public void ShowOnStartup()
    {
        ShowAndFocus(null);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (string.IsNullOrEmpty(SearchBox.Text))
            {
                HideWindow();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// 显示窗口并把焦点放到输入框。text 非空时会覆盖当前输入。
    /// </summary>
    public void ShowAndFocus(string? text, bool autoQuery = false)
    {
        _suppressHide = true;

        if (!string.IsNullOrWhiteSpace(text))
        {
            SearchBox.Text = text;
        }

        PositionWindow();

        Show();
        Activate();
        Topmost = true;

        SearchBox.Focus();
        SearchBox.SelectAll();

        _lastShownUtc = DateTime.UtcNow;
        _suppressHide = false;

        if (autoQuery)
        {
            Dispatcher.BeginInvoke(
                new Action(() => _ = RunQueryAsync()),
                DispatcherPriority.Background);
        }
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.14;
    }

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标在拖动开始前已释放，忽略
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var empty = string.IsNullOrEmpty(SearchBox.Text);
        Placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (empty)
        {
            EmptyState.Visibility = Visibility.Visible;
            ResultScroller.Visibility = Visibility.Collapsed;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HideWindow();
                e.Handled = true;
                break;

            case Key.Enter:
                _ = RunQueryAsync();
                e.Handled = true;
                break;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_suppressHide) return;
        if ((DateTime.UtcNow - _lastShownUtc).TotalMilliseconds < 300) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsActive) HideWindow();
        };
        timer.Start();
    }

    public void HideWindow()
    {
        if (_suppressHide) return;
        Hide();
    }

    private async Task RunQueryAsync()
    {
        var text = SearchBox.Text.Trim();

        if (string.IsNullOrEmpty(text))
        {
            EmptyState.Visibility = Visibility.Visible;
            ResultScroller.Visibility = Visibility.Collapsed;
            StatusText.Text = "输入单词开始查询";
            return;
        }

        ResultPanel.Children.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        ResultScroller.Visibility = Visibility.Visible;
        ResultScroller.ScrollToTop();

        var service = LookupService.Instance;
        if (!service.HasDictionary)
        {
            RenderMissingDictionary();
            return;
        }

        var entry = service.Lookup(text);
        if (entry is not null)
        {
            RenderEntry(entry);
            StatusText.Text = $"查词：{entry.Word}";
            return;
        }

        await TranslateAndRenderAsync(text);
    }

    private async Task TranslateAndRenderAsync(string text)
    {
        var service = LookupService.Instance;

        if (!service.HasTranslator)
        {
            RenderMessage(text, "未配置 DeepSeek API key，无法翻译长句。");
            StatusText.Text = "缺少 API key";
            return;
        }

        RenderMessage(text, "翻译中…");
        StatusText.Text = "翻译中…";

        try
        {
            var translation = await service.TranslateAsync(text);

            ResultPanel.Children.Clear();
            RenderTranslation(text, translation);
            StatusText.Text = $"翻译：{Shorten(text)}";
        }
        catch (Exception ex)
        {
            App.Log("translate failed: " + ex);
            ResultPanel.Children.Clear();
            RenderMessage(text, "翻译失败：" + ex.Message);
            StatusText.Text = "翻译失败";
        }
    }

    private void RenderMissingDictionary()
    {
        ResultPanel.Children.Add(new TextBlock
        {
            Text = "词库未找到",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("ThemeTextPrimary"),
        });
        ResultPanel.Children.Add(new TextBlock
        {
            Text = "dict.bin 应位于程序目录。运行 tools/build-dict.py 可从 ECDICT 生成。",
            FontSize = 13,
            Foreground = ThemeBrush("ThemeTextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });
        StatusText.Text = "词库缺失";
    }

    private static readonly Regex PosPrefixRegex =
        new(@"^([a-zA-Z]+\.)\s*", RegexOptions.Compiled);

    private void RenderEntry(DictionaryEntry entry)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };

        header.Children.Add(new TextBlock
        {
            Text = entry.Word,
            FontSize = 32,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("ThemeTextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (!string.IsNullOrEmpty(entry.Phonetic))
        {
            header.Children.Add(new TextBlock
            {
                Text = $"/{entry.Phonetic}/",
                FontSize = 15,
                Foreground = ThemeBrush("ThemeTextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            });
        }

        header.Children.Add(CreateSpeakButton(entry.Word));
        ResultPanel.Children.Add(header);

        foreach (var line in entry.SenseLines)
        {
            ResultPanel.Children.Add(CreateSenseCard(line));
        }
    }

    /// <summary>把 "n. 善行, 好处" 这类释义渲染成带词性胶囊的卡片。</summary>
    private Border CreateSenseCard(string line)
    {
        var match = PosPrefixRegex.Match(line);
        var pos = match.Success ? match.Groups[1].Value : string.Empty;
        var body = match.Success ? line[match.Length..] : line;

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        if (!string.IsNullOrEmpty(pos))
        {
            row.Children.Add(new Border
            {
                Background = ThemeBrush("ThemeSenseBorder"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 2, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = pos,
                    FontSize = 11,
                    Foreground = ThemeBrush("ThemeAccent"),
                },
            });
        }

        row.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 15,
            Foreground = ThemeBrush("ThemeTextPrimary"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24,
        });

        return new Border
        {
            Style = (Style)FindResource("GlassCardStyle"),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 9, 0, 0),
            Child = row,
        };
    }

    private void RenderTranslation(string source, string translation)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };

        header.Children.Add(new TextBlock
        {
            Text = source,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("ThemeTextPrimary"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 540,
            VerticalAlignment = VerticalAlignment.Center,
        });

        header.Children.Add(CreateSpeakButton(source));
        ResultPanel.Children.Add(header);

        ResultPanel.Children.Add(new Border
        {
            Background = ThemeBrush("ThemeSenseCard"),
            BorderBrush = ThemeBrush("ThemeSenseBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 14, 0, 0),
            Child = new TextBlock
            {
                Text = translation,
                FontSize = 16,
                Foreground = ThemeBrush("ThemeTextPrimary"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 26,
            },
        });

        // 译文朗读按钮（中文语音）
        var speakTranslation = CreateSpeakButton(
            translation, "▶ 朗读译文", SpeechService.ChineseLcid);
        speakTranslation.Margin = new Thickness(0, 12, 0, 0);
        speakTranslation.HorizontalAlignment = HorizontalAlignment.Left;
        ResultPanel.Children.Add(speakTranslation);
    }

    private void RenderMessage(string text, string message)
    {
        ResultPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("ThemeTextPrimary"),
            TextWrapping = TextWrapping.Wrap,
        });

        ResultPanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = ThemeBrush("ThemeTextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
    }

    private Border CreateSpeakButton(
        string text,
        string label = "▶ 发音",
        string lcid = SpeechService.EnglishLcid)
    {
        var button = new Border
        {
            Child = new TextBlock { Text = label, FontSize = 12, Foreground = ThemeBrush("ThemeTextPrimary") },
            Background = ThemeBrush("ThemeSenseBorder"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };

        button.MouseLeftButtonUp += (_, _) => SpeechService.Speak(text, lcid);
        return button;
    }

    private static string Shorten(string text)
        => text.Length <= 24 ? text : text[..24] + "…";

    private static SolidColorBrush ThemeBrush(string key) => ThemeService.Get(key);

    private static SolidColorBrush Solid(string hex)
        => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
