using System.Runtime.InteropServices;
using LexiPop.Interop;

namespace LexiPop.Services;

/// <summary>
/// 全局低级鼠标钩子：检测"按下 → 拖动 → 抬起"的选区动作。
/// 只在拖拽距离超过阈值时触发，避免普通点击误报。
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int DragThresholdSquared = 9; // 3px 的平方

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public NativeMethods.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook;
    private bool _disposed;

    private NativeMethods.POINT _downPoint;
    private bool _leftDown;

    /// <summary>拖拽选词结束后触发，参数为松开时的屏幕坐标（物理像素）。</summary>
    public event Action<int, int>? SelectionDragged;

    /// <summary>任意左键抬起（无论是否拖拽），用于收起浮窗。</summary>
    public event Action<int, int>? LeftButtonUp;

    public bool IsActive => _hook != IntPtr.Zero;

    public MouseHook()
    {
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = wParam.ToInt32();
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                if (message == WM_LBUTTONDOWN)
                {
                    _downPoint = info.pt;
                    _leftDown = true;
                }
                else if (message == WM_LBUTTONUP && _leftDown)
                {
                    _leftDown = false;

                    LeftButtonUp?.Invoke(info.pt.X, info.pt.Y);

                    var dx = info.pt.X - _downPoint.X;
                    var dy = info.pt.Y - _downPoint.Y;

                    if (dx * dx + dy * dy > DragThresholdSquared)
                    {
                        SelectionDragged?.Invoke(info.pt.X, info.pt.Y);
                    }
                }
            }
            catch
            {
                // 钩子回调绝不能抛异常，否则会卡住系统鼠标
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
