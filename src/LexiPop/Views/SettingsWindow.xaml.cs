using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LexiPop.Interop;
using LexiPop.Services;

namespace LexiPop.Views;

/// <summary>
/// 设置窗口。保存后写回 settings.json，并通过 SettingsApplied 通知 App 重新应用。
/// </summary>
public partial class SettingsWindow : Window
{
    public event Action? SettingsApplied;

    private readonly string _originalThemeId;
    private string? _editingThemeId;
    private bool _loading;
    private bool _savedSettings;

    public SettingsWindow()
    {
        InitializeComponent();
        _originalThemeId = ThemeService.Current.Id;
        SourceInitialized += OnSourceInitialized;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        LoadSettings();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyWindowMaterial(hwnd);

        var corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        ThemeService.Changed += OnThemeServiceChanged;
    }

    /// <summary>主题变化：重新应用窗口材质（与主窗口逻辑一致）。</summary>
    private void OnThemeServiceChanged()
    {
        ApplyWindowMaterial(new WindowInteropHelper(this).Handle);
    }

    /// <summary>窗口透明由 XAML 的 AllowsTransparency 控制；这里只做 Acrylic 增强尝试。</summary>
    private void ApplyWindowMaterial(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        if (ThemeService.Current.IsGlass)
        {
            var color = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(ThemeService.Current.GlassSurface);
            var tint = (uint)color.A << 24 | (uint)color.B << 16 |
                       (uint)color.G << 8 | color.R;
            WindowEffects.TryEnableAcrylic(hwnd, tint);
        }
        else
        {
            WindowEffects.Disable(hwnd);
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        var settings = AppSettings.Load();

        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        TranslateHotkeyBox.Text = settings.HotkeyTranslate;
        RecallHotkeyBox.Text = settings.HotkeyRecall;

        ProviderCombo.ItemsSource = ModelProviders.All;
        ProviderCombo.DisplayMemberPath = "Name";
        ProviderCombo.SelectedItem = ModelProviders.Find(settings.ProviderId);
        ApiKeyBox.Text = settings.ApiKey;
        EndpointBox.Text = settings.Endpoint;
        ModelListEndpointBox.Text = settings.ModelListEndpoint;
        ModelCombo.Text = settings.Model;

        EnableTranslationCheck.IsChecked = settings.EnableTranslation;
        ShowSelectionButtonCheck.IsChecked = settings.ShowSelectionButton;
        AutoPronounceCheck.IsChecked = settings.AutoPronounceWord;

        var count = LookupService.Instance.DictionaryCount;
        DictInfo.Text = count > 0
            ? $"词库：{count:N0} 条"
            : "词库：未加载（dict.bin 不存在）";
        RefreshCacheInfo();

        RefreshThemes();
        ThemeCombo.SelectedItem =
            ThemeService.AvailableThemes().FirstOrDefault(p => p.Id == settings.ThemeId)
            ?? ThemeService.Presets[0];
        _loading = false;
    }

    private void OnWindowActivated(object? sender, EventArgs e) => RefreshCacheInfo();

    private void RefreshCacheInfo()
    {
        var stats = LookupService.Instance.GetTranslationCacheStatistics();
        CacheInfo.Text = $"翻译缓存：{stats.Count:N0} 条 · 文件大小 {FormatFileSize(stats.FileBytes)}";
        CachePathInfo.Text = $"缓存文件：{stats.FilePath}";
    }

    private static string FormatFileSize(long? bytes)
    {
        if (bytes is null) return "无法读取";
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes / (1024d * 1024):0.0} MB";
    }

    private void RefreshThemes()
    {
        ThemeCombo.ItemsSource = ThemeService.AvailableThemes();
        ThemeCombo.DisplayMemberPath = "Name";
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeCombo.SelectedItem is ThemePreset preset)
        {
            ThemeService.Apply(preset.Id);
        }
    }

    private void OnNewTheme(object sender, RoutedEventArgs e)
    {
        var source = ThemeCombo.SelectedItem as ThemePreset ?? ThemeService.Presets[0];
        _editingThemeId = null;
        ThemeCssBox.Text = CustomThemeService.CreateTemplate(source);
        ThemeEditor.Visibility = Visibility.Visible;
        ThemeCssBox.Focus();
    }

    private void OnEditTheme(object sender, RoutedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ThemePreset { Id: var id } ||
            !id.StartsWith("custom-", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "内置主题不可修改；可以先点「新建主题」复制当前配色。",
                "LexiPop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _editingThemeId = id;
        ThemeCssBox.Text = CustomThemeService.ReadCss(id) ?? string.Empty;
        ThemeEditor.Visibility = Visibility.Visible;
        ThemeCssBox.Focus();
    }

    private void OnSaveTheme(object sender, RoutedEventArgs e) => SaveTheme();

    private bool SaveTheme()
    {
        if (!CustomThemeService.TrySave(ThemeCssBox.Text, _editingThemeId,
                out var theme, out var error))
        {
            MessageBox.Show(this, error, "CSS 主题", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _editingThemeId = theme!.Id;
        RefreshThemes();
        ThemeCombo.SelectedItem = ThemeService.AvailableThemes().First(p => p.Id == theme.Id);
        ThemeService.Apply(theme.Id);
        ThemeStatus.Text = $"主题已保存：{CustomThemeService.DirectoryPath}";
        return true;
    }

    private void OnCloseThemeEditor(object sender, RoutedEventArgs e) =>
        ThemeEditor.Visibility = Visibility.Collapsed;

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProviderCombo.SelectedItem is not ModelProvider provider) return;
        EndpointBox.Text = provider.ChatEndpoint;
        ModelListEndpointBox.Text = provider.ModelsEndpoint;
        ModelCombo.ItemsSource = null;
        ModelCombo.Text = string.Empty;
        ApiKeyBox.Text = string.Empty;
        ModelFetchStatus.Text = provider.Id == "custom"
            ? "请填写兼容 Chat Completions 的接口和模型列表网址。"
            : "需要时输入该提供方的 API Key，再抓取模型。";
    }

    private async void OnFetchModels(object sender, RoutedEventArgs e)
    {
        var previous = ModelCombo.Text.Trim();
        FetchModelsButton.IsEnabled = false;
        ModelFetchStatus.Text = "正在抓取模型…";
        try
        {
            var key = ApiKeyBox.Text.Trim();
            var selectedProvider = (ProviderCombo.SelectedItem as ModelProvider)?.Id;
            var saved = AppSettings.Load();
            if (key.Length == 0 && selectedProvider == saved.ProviderId)
                key = saved.ResolveApiKey();

            var models = await ModelProviders.FetchModelsAsync(
                ModelListEndpointBox.Text.Trim(), key);
            ModelCombo.ItemsSource = models;
            ModelCombo.Text = previous;
            ModelFetchStatus.Text = $"已获取 {models.Count} 个模型；请选择支持文本聊天的模型。";
        }
        catch (Exception ex)
        {
            ModelFetchStatus.Text = ex.Message;
        }
        finally
        {
            FetchModelsButton.IsEnabled = true;
        }
    }

    private void OnSelectionHelp(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "在其他程序中拖选文字，开启按钮时鼠标旁会出现「译」；点击后显示本地词典结果或长句译文。关闭按钮只隐藏这个入口，不影响划词热键。结果小窗快捷键可隐藏小窗，再按一次恢复上次结果。",
            "划词翻译", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnLongTextHelp(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "单词优先查本地词典；词典未命中的文本会发送给选定的模型提供方翻译。关闭此项后仍可离线查词，长句不会发送到接口。",
            "长句翻译", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None) return;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(key.ToString());
        box.Text = string.Join("+", parts);
        e.Handled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (EnableTranslationCheck.IsChecked == true &&
            !ModelProviders.IsValidEndpoint(EndpointBox.Text.Trim()))
        {
            MessageBox.Show(this, "请填写完整的聊天接口端点网址。", "LexiPop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (EnableTranslationCheck.IsChecked == true && string.IsNullOrWhiteSpace(ModelCombo.Text))
        {
            MessageBox.Show(this, "启用长句翻译时，请选择或输入模型 ID。", "LexiPop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var translateHotkey = HotkeyParser.Parse(TranslateHotkeyBox.Text.Trim());
        var recallHotkey = HotkeyParser.Parse(RecallHotkeyBox.Text.Trim());
        if (translateHotkey is null || recallHotkey is null ||
            translateHotkey == recallHotkey)
        {
            MessageBox.Show(this, "请设置两个不同的有效快捷键。", "LexiPop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ThemeEditor.Visibility == Visibility.Visible && !SaveTheme()) return;

        var settings = AppSettings.Load();

        settings.HotkeyTranslate = TranslateHotkeyBox.Text.Trim();
        settings.HotkeyRecall = RecallHotkeyBox.Text.Trim();
        settings.ProviderId = (ProviderCombo.SelectedItem as ModelProvider)?.Id ?? "deepseek";
        settings.ApiKey = ApiKeyBox.Text.Trim();
        settings.Endpoint = EndpointBox.Text.Trim();
        settings.ModelListEndpoint = ModelListEndpointBox.Text.Trim();
        settings.Model = ModelCombo.Text.Trim();
        settings.EnableTranslation = EnableTranslationCheck.IsChecked == true;
        settings.ShowSelectionButton = ShowSelectionButtonCheck.IsChecked == true;
        settings.AutoPronounceWord = AutoPronounceCheck.IsChecked == true;
        settings.ThemeId = (ThemeCombo.SelectedItem as ThemePreset)?.Id ?? "light";

        if (!AppSettings.TrySave(settings))
        {
            MessageBox.Show(this, "设置保存失败。", "LexiPop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var wantAutoStart = AutoStartCheck.IsChecked == true;
        if (wantAutoStart != AutoStart.IsEnabled())
        {
            AutoStart.Toggle();
        }

        SettingsApplied?.Invoke();
        _savedSettings = true;
        Close();
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        var removed = LookupService.Instance.ClearTranslationCache();
        RefreshCacheInfo();
        var fileBytes = LookupService.Instance.GetTranslationCacheStatistics().FileBytes;
        var fileRemaining = fileBytes is null or > 0;
        var message = fileBytes switch
        {
            null => $"已清除 {removed} 条内存缓存，但无法确认磁盘缓存文件是否已删除。",
            > 0 => $"已清除 {removed} 条内存缓存，但磁盘缓存文件未能删除，请检查文件权限。",
            _ => removed > 0 ? $"已清除 {removed} 条翻译缓存。" : "暂无翻译缓存。"
        };
        MessageBox.Show(this, message, "LexiPop", MessageBoxButton.OK,
            fileRemaining ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeService.Changed -= OnThemeServiceChanged;
        if (!_savedSettings) ThemeService.Apply(_originalThemeId);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }
}
