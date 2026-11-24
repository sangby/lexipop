using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LexiPop.Interop;
using static LexiPop.Interop.NativeMethods;

namespace LexiPop.Services;

/// <summary>
/// 系统托盘图标，手写 Shell_NotifyIcon 以便拿到真实错误码。
/// 注册失败时不抛异常，只记录日志并继续运行（热键与窗口不受影响）。
/// </summary>
public sealed class TrayService : IDisposable
{
    private const uint TrayIconId = 1;

    private const int MenuLookup = 1001;
    private const int MenuAutoStart = 1002;
    private const int MenuExit = 1003;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const uint NIIF_INFO = 0x1;

    private readonly MessageWindow _host;
    private readonly Action<string> _log;
    private NOTIFYICONDATA _data;
    private IntPtr _hIcon;
    private bool _added;
    private bool _ownsIcon;
    private bool _disposed;

    public event Action? ShowRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public bool IsRegistered => _added;

    public TrayService(MessageWindow host, Action<string> log)
    {
        _host = host;
        _log = log;
        _host.TrayMouseMessage += OnTrayMouseMessage;

        _hIcon = CreateTrayIconHandle();
        _log($"tray: iconHandle=0x{_hIcon:X}");

        _data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _host.Handle,
            uID = TrayIconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.TRAY_CALLBACK_MESSAGE,
            hIcon = _hIcon,
            szTip = "LexiPop 词典",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _data);

        if (!_added)
        {
            var error = Marshal.GetLastWin32Error();
            _log($"tray: NIM_ADD failed, error={error}, hwnd=0x{_host.Handle:X}, cbSize={_data.cbSize}");
            return;
        }

        _log("tray: NIM_ADD ok");

        _data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _data);
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;

        var data = _data;
        data.uFlags = NativeMethods.NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void OnTrayMouseMessage(int mouseMessage)
    {
        switch (mouseMessage)
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
                ShowRequested?.Invoke();
                break;

            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();

        var lookup = new MenuItem { Header = "查词" };
        lookup.Click += (_, _) => ShowRequested?.Invoke();
        menu.Items.Add(lookup);

        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var autoStart = new MenuItem
        {
            Header = "开机自启",
            IsCheckable = true,
            IsChecked = AutoStart.IsEnabled(),
        };
        autoStart.Click += (_, _) => AutoStart.Toggle();
        menu.Items.Add(autoStart);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>生成蓝底白字 L 的 32×32 托盘图标；失败时回落到系统默认图标。</summary>
    private IntPtr CreateTrayIconHandle()
    {
        try
        {
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using var background = new SolidBrush(Color.FromArgb(107, 138, 254));
                graphics.FillEllipse(background, 1, 1, 30, 30);

                using var font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
                using var white = new SolidBrush(Color.White);
                var size = graphics.MeasureString("L", font);
                graphics.DrawString(
                    "L", font, white,
                    (32 - size.Width) / 2f,
                    (32 - size.Height) / 2f);
            }

            _ownsIcon = true;
            return bitmap.GetHicon();
        }
        catch
        {
            _ownsIcon = false;
            return NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(32512));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _host.TrayMouseMessage -= OnTrayMouseMessage;

        if (_added)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _data);
        }

        if (_hIcon != IntPtr.Zero && _ownsIcon)
        {
            NativeMethods.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
