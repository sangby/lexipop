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

    public SettingsWindow()
    {
        InitializeComponent();
        _originalThemeId = ThemeService.Current.Id;
        SourceInitialized += OnSourceInitialized;
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
            WindowEffects.TryEnableAcrylic(hwnd, 0x80FFFFFF);
        }
        else
        {
            WindowEffects.Disable(hwnd);
        }
    }

    private void LoadSettings()
    {
        var settings = AppSettings.Load();

        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        ShowOnStartupCheck.IsChecked = settings.ShowWindowOnStartup;

        ToggleHotkeyBox.Text = settings.HotkeyToggle;
        TranslateHotkeyBox.Text = settings.HotkeyTranslate;

        ApiKeyBox.Text = settings.DeepSeekApiKey;
        EndpointBox.Text = settings.DeepSeekEndpoint;
        ModelBox.Text = settings.DeepSeekModel;

        EnableTranslationCheck.IsChecked = settings.EnableTranslation;
        EnablePopupCheck.IsChecked = settings.EnableSelectionPopup;

        var count = LookupService.Instance.DictionaryCount;
        DictInfo.Text = count > 0
            ? $"词库：{count:N0} 条"
            : "词库：未加载（dict.bin 不存在）";

        ThemeCombo.ItemsSource = ThemeService.Presets;
        ThemeCombo.DisplayMemberPath = "Name";
        ThemeCombo.SelectedItem =
            ThemeService.Presets.FirstOrDefault(p => p.Id == settings.ThemeId)
            ?? ThemeService.Presets[0];
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ThemePreset preset)
        {
            ThemeService.Apply(preset.Id);
        }
    }

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
        var settings = AppSettings.Load();

        settings.ShowWindowOnStartup = ShowOnStartupCheck.IsChecked == true;
        settings.HotkeyToggle = ToggleHotkeyBox.Text.Trim();
        settings.HotkeyTranslate = TranslateHotkeyBox.Text.Trim();
        settings.DeepSeekApiKey = ApiKeyBox.Text.Trim();
        settings.DeepSeekEndpoint = EndpointBox.Text.Trim();
        settings.DeepSeekModel = ModelBox.Text.Trim();
        settings.EnableTranslation = EnableTranslationCheck.IsChecked == true;
        settings.EnableSelectionPopup = EnablePopupCheck.IsChecked == true;
        settings.ThemeId = (ThemeCombo.SelectedItem as ThemePreset)?.Id ?? "graphite";

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
        Close();
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        var removed = LookupService.Instance.ClearTranslationCache();
        MessageBox.Show(this, removed > 0 ? $"已清除 {removed} 条翻译缓存。" : "暂无翻译缓存。",
            "LexiPop", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        ThemeService.Apply(_originalThemeId);
        ThemeService.Changed -= OnThemeServiceChanged;
        Close();
    }

    private void OnClose(object sender, MouseButtonEventArgs e)
    {
        ThemeService.Apply(_originalThemeId);
        ThemeService.Changed -= OnThemeServiceChanged;
        Close();
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
