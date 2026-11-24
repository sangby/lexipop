using System.Windows.Interop;
using LexiPop.Interop;

namespace LexiPop.Services;

/// <summary>
/// 一个不可见的顶层窗口，专门接收全局热键、托盘回调与单实例广播消息。
/// 不依赖可见窗口，后台运行时消息循环照常工作。
/// </summary>
public sealed class MessageWindow : IDisposable
{
    public const int HotkeyTranslate = 2;

    private readonly HwndSource _source;
    private bool _disposed;

    public IntPtr Handle => _source.Handle;

    /// <summary>热键被按下，参数是热键 id。</summary>
    public event Action<int>? HotkeyPressed;

    /// <summary>托盘图标被点击，参数是原始鼠标消息（WM_LBUTTONDBLCLK / WM_CONTEXTMENU 等）。</summary>
    public event Action<int>? TrayMouseMessage;

    /// <summary>另一个实例启动后请求打开设置。</summary>
    public event Action? ShowRequested;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("LexiPop.Message")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool RegisterHotkey(int id, uint modifiers, uint virtualKey)
        => NativeMethods.RegisterHotKey(Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);

    public void UnregisterHotkey(int id) => NativeMethods.UnregisterHotKey(Handle, id);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_HOTKEY:
                HotkeyPressed?.Invoke(wParam.ToInt32());
                handled = true;
                break;

            case NativeMethods.TRAY_CALLBACK_MESSAGE:
                // NOTIFYICON_VERSION_4：LOWORD(lParam) 是鼠标消息，HIWORD(lParam) 是图标 id
                TrayMouseMessage?.Invoke((int)(lParam.ToInt64() & 0xFFFF));
                handled = true;
                break;

            case int m when m == SingleInstance.ShowMessageId:
                ShowRequested?.Invoke();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NativeMethods.UnregisterHotKey(Handle, HotkeyTranslate);

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
