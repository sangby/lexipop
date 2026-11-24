using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using LexiPop.Interop;
using LexiPop.Services;
using LexiPop.Views;

namespace LexiPop;

public partial class App : Application
{
    private MessageWindow? _messageWindow;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private MouseHook? _mouseHook;
    private SelectionPopup? _selectionPopup;
    private SettingsWindow? _settingsWindow;
    private bool _toggleRegistered;
    private bool _translateRegistered;
    private bool _ownsMutex;

    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "lexipop.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                LogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("Unhandled: " + args.ExceptionObject);

        DispatcherUnhandledException += (_, args) =>
        {
            Log("Dispatcher: " + args.Exception);
            args.Handled = true;
        };

        Log("=== start ===");
        Log($"integrity={NativeMethods.GetCurrentIntegrityLevel()}, parent={NativeMethods.GetParentProcessName()}, elevated={Environment.IsPrivilegedProcess}");

        _ownsMutex = SingleInstance.TryAcquire();
        Log($"mutex owned={_ownsMutex}");

        if (!_ownsMutex)
        {
            SingleInstance.NotifyExistingInstance();
            Shutdown(0);
            return;
        }

        try
        {
            _messageWindow = new MessageWindow();
            Log("message window created");

            _mainWindow = new MainWindow();
            Log("main window created");

            _messageWindow.HotkeyPressed += OnHotkeyPressed;
            _messageWindow.ShowRequested += ShowWindow;

            var settings = AppSettings.Load();
            if (!File.Exists(AppSettings.FilePath) && AppSettings.TrySave(settings))
            {
                Log($"settings template created: {AppSettings.FilePath}");
            }

            ThemeService.Apply(settings.ThemeId);
            Log($"theme: {settings.ThemeId}");

            Log($"settings: deepseek key configured={!string.IsNullOrEmpty(settings.ResolveDeepSeekKey())}");

            try
            {
                _tray = new TrayService(_messageWindow!, Log);
                _tray.ShowRequested += ShowWindow;
                _tray.SettingsRequested += OpenSettings;
                _tray.ExitRequested += () => Shutdown(0);
                Log($"tray ready: registered={_tray.IsRegistered}");
            }
            catch (Exception ex)
            {
                Log("tray unavailable: " + ex.Message);
            }

            RegisterHotkeys(settings);

            if (!_toggleRegistered || !_translateRegistered)
            {
                MessageBox.Show(
                    "部分全局热键被其他程序占用：\n" +
                    $"  {settings.HotkeyToggle}：{(_toggleRegistered ? "可用" : "被占用")}\n" +
                    $"  {settings.HotkeyTranslate}：{(_translateRegistered ? "可用" : "被占用")}\n\n" +
                    "可在托盘右键菜单「设置」里修改。",
                    "LexiPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // 划词浮窗：全局鼠标钩子检测拖拽选区（受设置开关控制）
            ApplySelectionPopup(settings.EnableSelectionPopup);

            _tray?.ShowBalloon(
                "LexiPop 已启动",
                $"{settings.HotkeyToggle} 唤出，{settings.HotkeyTranslate} 划词翻译。");
            Log("startup complete");

            // 带参数启动时直接查询该词（便于测试）；否则按设置决定是否显示窗口。
            if (e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0]))
            {
                Log($"startup query: {e.Args[0]}");
                _mainWindow.ShowAndFocus(e.Args[0], autoQuery: true);
            }
            else if (settings.ShowWindowOnStartup)
            {
                _mainWindow.ShowOnStartup();
                Log("startup window shown");
            }
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);

            MessageBox.Show(
                $"启动失败：{ex.Message}",
                "LexiPop",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void ShowWindow()
    {
        _mainWindow?.ShowAndFocus(null);
    }

    private void RegisterHotkeys(AppSettings settings)
    {
        _messageWindow?.UnregisterHotkey(MessageWindow.HotkeyToggle);
        _messageWindow?.UnregisterHotkey(MessageWindow.HotkeyTranslate);

        var toggle = HotkeyParser.Parse(settings.HotkeyToggle)
                     ?? (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x44u);
        var translate = HotkeyParser.Parse(settings.HotkeyTranslate)
                        ?? (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x53u);

        _toggleRegistered = _messageWindow?.RegisterHotkey(
            MessageWindow.HotkeyToggle, toggle.Item1, toggle.Item2) ?? false;
        _translateRegistered = _messageWindow?.RegisterHotkey(
            MessageWindow.HotkeyTranslate, translate.Item1, translate.Item2) ?? false;

        Log($"hotkeys: toggle={_toggleRegistered} ({settings.HotkeyToggle}), " +
            $"translate={_translateRegistered} ({settings.HotkeyTranslate})");
    }

    private void ApplySelectionPopup(bool enabled)
    {
        if (!enabled)
        {
            _mouseHook?.Dispose();
            _mouseHook = null;
            _selectionPopup?.Hide();
            Log("selection popup disabled");
            return;
        }

        if (_mouseHook is { IsActive: true }) return;

        _mouseHook = new MouseHook();
        if (_mouseHook.IsActive)
        {
            _mouseHook.SelectionDragged += OnSelectionDragged;
            _mouseHook.LeftButtonUp += OnAnyLeftClick;
            Log("mouse hook active");
        }
        else
        {
            Log("mouse hook failed");
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.SettingsApplied += OnSettingsApplied;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnSettingsApplied()
    {
        var settings = AppSettings.Load();

        // 关键：重建翻译器，否则设置页填的 Key / 端点 / 模型不生效（要重启才行）
        LookupService.Instance.Reload();

        RegisterHotkeys(settings);
        ApplySelectionPopup(settings.EnableSelectionPopup);
        Log("settings applied");
    }

    /// <summary>任意左键抬起：点击浮窗外部时收起浮窗。</summary>
    private void OnAnyLeftClick(int x, int y)
    {
        _selectionPopup?.HideIfOutside(x, y);
    }

    /// <summary>
    /// 拖拽选词结束：延迟取词（等目标应用更新选区），然后显示浮窗按钮。
    /// UIA 读不到时回退到模拟 Ctrl+C 读剪贴板，覆盖 Typora / 微信这类 UIA 不完整的应用。
    /// </summary>
    private void OnSelectionDragged(int x, int y)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Task.Delay(40);

                var (text, diagnostics) = CaptureSelection(allowClipboard: true);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log($"popup: no selection ({diagnostics})");
                    return;
                }

                _selectionPopup ??= new SelectionPopup();
                _selectionPopup.ShowAt(x, y, text);
                Log($"popup: shown len={text.Length} ({diagnostics})");
            }
            catch (Exception ex)
            {
                Log("popup failed: " + ex.Message);
            }
        }));
    }

    private void OnHotkeyPressed(int id)
    {
        // 再按一次同一个热键：窗口可见且是活动窗口 → 收起。
        if (_mainWindow is { IsVisible: true } && _mainWindow.IsActive)
        {
            _mainWindow.HideWindow();
            Log($"hotkey {id} -> hide");
            return;
        }

        // 先取词，再显示窗口：窗口一显示就会抢走焦点，之后 UIA 读不到选区。
        var (selected, diagnostics) = CaptureSelection();
        Log($"hotkey {id} -> {diagnostics}");

        if (id == MessageWindow.HotkeyTranslate)
        {
            var hasSelection = !string.IsNullOrWhiteSpace(selected);
            _mainWindow?.ShowAndFocus(selected, autoQuery: hasSelection);
        }
        else
        {
            _mainWindow?.ShowAndFocus(selected);
        }
    }

    /// <summary>
    /// 读取当前选区，同时产生诊断字符串（写日志用）。
    /// FocusedElement 在浏览器里常常只返回窗口本身，所以同时从鼠标位置取元素，
    /// 并向上遍历祖先，直到找到支持 TextPattern 的控件。
    /// </summary>
    private static (string? Text, string Diagnostics) CaptureByUia()
    {
        var diagnostics = new StringBuilder();

        try
        {
            NativeMethods.GetCursorPos(out var cursor);
            var mousePoint = new System.Windows.Point(cursor.X, cursor.Y);

            var candidates = new List<(AutomationElement Element, string Source)>();

            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                candidates.Add((focused, "focus"));
            }

            try
            {
                var underMouse = AutomationElement.FromPoint(mousePoint);
                if (underMouse is not null)
                {
                    candidates.Add((underMouse, "mouse"));
                }
            }
            catch (Exception ex)
            {
                diagnostics.Append("fromPoint=").Append(ex.GetType().Name).Append(';');
            }

            foreach (var (element, source) in candidates)
            {
                AutomationElement? current = element;

                for (var depth = 0; depth < 8 && current is not null; depth++)
                {
                    if (current.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                    {
                        var textPattern = (TextPattern)pattern;
                        var ranges = textPattern.GetSelection();

                        if (ranges.Length > 0)
                        {
                            var text = ranges[0].GetText(500)?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                diagnostics
                                    .Append(source).Append('+').Append(depth)
                                    .Append(":len=").Append(text.Length);
                                return (text, diagnostics.ToString());
                            }
                        }
                    }

                    try
                    {
                        current = TreeWalker.ControlViewWalker.GetParent(current);
                    }
                    catch
                    {
                        current = null;
                    }
                }
            }

            diagnostics.Append("no-selection");
            return (null, diagnostics.ToString());
        }
        catch (Exception ex)
        {
            diagnostics.Append("ex=").Append(ex.GetType().Name).Append(':').Append(ex.Message);
            return (null, diagnostics.ToString());
        }
    }

    /// <summary>
    /// 取词：UIA 优先，失败可选回退到模拟 Ctrl+C 读剪贴板。
    /// 划词浮窗场景传 allowClipboard=false，避免拖窗口/拖文件时误触发复制。
    /// </summary>
    private static (string? Text, string Diagnostics) CaptureSelection(bool allowClipboard = true)
    {
        var (uiaText, uiaDiagnostics) = CaptureByUia();
        if (!string.IsNullOrWhiteSpace(uiaText))
        {
            return (uiaText, "uia:" + uiaDiagnostics);
        }

        if (!allowClipboard)
        {
            return (null, $"uia:{uiaDiagnostics}|clip=off");
        }

        var clipboardText = ClipboardCapture.TryCapture();
        return (clipboardText, $"uia:{uiaDiagnostics}|clip={clipboardText?.Length ?? 0}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("=== exit ===");

        _mouseHook?.Dispose();
        _tray?.Dispose();
        _messageWindow?.Dispose();

        if (_ownsMutex) SingleInstance.Release();

        base.OnExit(e);
    }
}
