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
    private MouseHook? _mouseHook;
    private SelectionPopup? _selectionPopup;
    private SettingsWindow? _settingsWindow;
    private bool _translateRegistered;
    private bool _recallRegistered;
    private bool _showSelectionButton = true;
    private bool _ownsMutex;
    private string _lastCaptureStatus = "尚未尝试取词";

    public (bool Tray, bool MouseHook, bool TranslateHotkey, bool RecallHotkey,
        string LastCapture) GetDiagnosticStatus() =>
        (_tray?.IsRegistered == true, _mouseHook?.IsActive == true,
            _translateRegistered, _recallRegistered, _lastCaptureStatus);

    private void RecordCapture(string? text, string diagnostics)
    {
        var source = diagnostics.StartsWith("uia:", StringComparison.Ordinal) &&
                     !diagnostics.Contains("|clip=", StringComparison.Ordinal)
            ? "UI Automation"
            : text is not null ? "剪贴板回退" : "未取到文本";
        _lastCaptureStatus = $"{DateTime.Now:HH:mm:ss} · {source} · {(text?.Length ?? 0)} 字符";
    }

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
        var integrity = NativeMethods.GetCurrentIntegrityLevel();
        Log($"integrity={integrity}, parent={NativeMethods.GetParentProcessName()}, elevated={Environment.IsPrivilegedProcess}");

        // 从受限宿主直接打开 exe 时会继承低完整性：AppData 写入、托盘注册和跨进程取词都会失败。
        // 在占用单实例 mutex 前提示并退出，避免它挡住随后从文件资源管理器启动的正常实例。
        if (integrity.StartsWith("low", StringComparison.OrdinalIgnoreCase) ||
            integrity.StartsWith("untrusted", StringComparison.OrdinalIgnoreCase))
        {
            Log("startup blocked: low integrity");
            MessageBox.Show(
                "LexiPop 当前以受限权限启动，无法访问主题目录、系统托盘和其他程序的选中文本。\n\n请从 Windows 文件资源管理器中双击 LexiPop.exe 启动。",
                "LexiPop 启动受限",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

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

            _messageWindow.HotkeyPressed += OnHotkeyPressed;
            _messageWindow.ShowRequested += OpenSettings;

            var settings = AppSettings.Load();
            if (!File.Exists(AppSettings.FilePath) && AppSettings.TrySave(settings))
            {
                Log($"settings template created: {AppSettings.FilePath}");
            }

            ThemeService.Apply(settings.ThemeId);
            Log($"theme: {settings.ThemeId}");

            Log($"settings: provider={settings.ProviderId} key configured={!string.IsNullOrEmpty(settings.ResolveApiKey())}");

            try
            {
                _tray = new TrayService(_messageWindow!, Log);
                _tray.SettingsRequested += OpenSettings;
                _tray.ExitRequested += () => Shutdown(0);
                Log($"tray ready: registered={_tray.IsRegistered}");
            }
            catch (Exception ex)
            {
                Log("tray unavailable: " + ex.Message);
            }

            if (_tray?.IsRegistered != true)
            {
                MessageBox.Show(
                    $"系统托盘图标注册失败（错误码 {_tray?.LastError ?? 0}）。LexiPop 将退出，以免留下无法从托盘关闭的后台实例。\n\n请从 Windows 文件资源管理器重新启动；若问题持续，请查看程序目录中的 lexipop.log。",
                    "LexiPop 托盘不可用",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            RegisterHotkeys(settings);

            if (!_translateRegistered)
            {
                MessageBox.Show(
                    $"划词热键 {settings.HotkeyTranslate} 被其他程序占用，可在托盘「设置」中修改。",
                    "LexiPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (!_recallRegistered)
            {
                MessageBox.Show(
                    $"结果恢复热键 {settings.HotkeyRecall} 被其他程序占用，可在托盘「设置」中修改。",
                    "LexiPop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // 鼠标钩子始终保留点击窗外收起卡片；设置只控制拖选后的小按钮。
            ApplySelectionButtonSetting(settings.ShowSelectionButton);

            var buttonHint = settings.ShowSelectionButton ? "拖选文字后点「译」；" : string.Empty;
            _tray?.ShowBalloon(
                "LexiPop 已启动",
                buttonHint + $"按 {settings.HotkeyTranslate} 翻译，按 {settings.HotkeyRecall} 隐藏或恢复结果小窗。");
            Log("startup complete");

            // 带参数启动可直接预览浮窗结果，便于调试与外部程序调用。
            if (e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0]))
            {
                NativeMethods.GetCursorPos(out var cursor);
                _selectionPopup ??= new SelectionPopup();
                _selectionPopup.ShowExpandedAt(cursor.X, cursor.Y, e.Args[0]);
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

    private void RegisterHotkeys(AppSettings settings)
    {
        _messageWindow?.UnregisterHotkey(MessageWindow.HotkeyTranslate);
        _messageWindow?.UnregisterHotkey(MessageWindow.HotkeyRecall);
        var translate = HotkeyParser.Parse(settings.HotkeyTranslate)
                        ?? (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x53u);

        _translateRegistered = _messageWindow?.RegisterHotkey(
            MessageWindow.HotkeyTranslate, translate.Item1, translate.Item2) ?? false;

        var recall = HotkeyParser.Parse(settings.HotkeyRecall)
                     ?? (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x52u);
        _recallRegistered = _messageWindow?.RegisterHotkey(
            MessageWindow.HotkeyRecall, recall.Item1, recall.Item2) ?? false;

        Log($"hotkey: translate={_translateRegistered} ({settings.HotkeyTranslate}), recall={_recallRegistered} ({settings.HotkeyRecall})");
    }

    private void ApplySelectionButtonSetting(bool showButton)
    {
        _showSelectionButton = showButton;
        if (!showButton) _selectionPopup?.HideButtonIfVisible();

        if (_mouseHook is { IsActive: true }) return;

        _mouseHook?.Dispose();
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

        // 重建翻译器，立即应用提供方、Key、端点和模型。
        LookupService.Instance.Reload();

        ThemeService.Apply(settings.ThemeId);
        RegisterHotkeys(settings);
        ApplySelectionButtonSetting(settings.ShowSelectionButton);
        if (!_translateRegistered || !_recallRegistered)
        {
            var unavailable = new List<string>();
            if (!_translateRegistered) unavailable.Add($"划词翻译：{settings.HotkeyTranslate}");
            if (!_recallRegistered) unavailable.Add($"结果小窗：{settings.HotkeyRecall}");
            MessageBox.Show(
                "以下快捷键注册失败，可能已被其他程序占用：\n" + string.Join("\n", unavailable) +
                "\n\n请在设置中换一个组合键。",
                "LexiPop 快捷键不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        if (!_showSelectionButton) return;
        Log($"selection drag: x={x}, y={y}");
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Task.Delay(40);
                if (!_showSelectionButton) return;

                var (text, diagnostics) = CaptureSelection(allowClipboard: true);
                RecordCapture(text, diagnostics);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log($"popup: no selection ({diagnostics})");
                    return;
                }

                if (!_showSelectionButton) return;
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
        if (id == MessageWindow.HotkeyRecall)
        {
            NativeMethods.GetCursorPos(out var lastCursor);
            var result = _selectionPopup?.ToggleLastResultAt(lastCursor.X, lastCursor.Y)
                         ?? LastResultToggleResult.Unavailable;
            Log($"recall hotkey: {result}");
            if (result == LastResultToggleResult.Unavailable)
                _tray?.ShowBalloon("暂无上次结果", "先完成一次划词查询或翻译。");
            return;
        }

        if (id != MessageWindow.HotkeyTranslate) return;

        // 先取词，再显示浮窗：浮窗出现后会改变 UIA 的焦点。
        var (selected, diagnostics) = CaptureSelection();
        RecordCapture(selected, diagnostics);
        Log($"hotkey {id} -> {diagnostics}");

        if (string.IsNullOrWhiteSpace(selected))
        {
            _tray?.ShowBalloon("未找到选中文本", "先选中文字，再按划词翻译热键。");
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        _selectionPopup ??= new SelectionPopup();
        _selectionPopup.ShowExpandedAt(cursor.X, cursor.Y, selected);
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
