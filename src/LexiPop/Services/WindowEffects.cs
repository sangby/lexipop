using System.Runtime.InteropServices;

namespace LexiPop.Services;

/// <summary>
/// 窗口级毛玻璃。
///
/// 方案说明（重要）：
/// - 之前尝试的 DWM API（DWMWA_SYSTEMBACKDROP_TYPE）在 WPF 中需要 WindowChrome /
///   WS_EX_NOREDIRECTIONBITMAP 才能生效，否则透明背景会渲染成黑底，已弃用。
/// - 这里改用未公开但稳定的 user32!SetWindowCompositionAttribute（WCA_ACCENT_POLICY），
///   它是 Win10 1803+ 与 Win11 通用的 Acrylic 实现，WPF 项目（如 FluentWPF、WPF-UI）
///   普遍采用，不需要 WindowChrome。
/// - 失败时调用方回退到不透明底色，保证不出现黑屏。
/// </summary>
public static class WindowEffects
{
    private const int WCA_ACCENT_POLICY = 19;

    // AccentState 取值
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// 开启 Acrylic 毛玻璃。
    /// </summary>
    /// <param name="hwnd">窗口句柄</param>
    /// <param name="tintColor">色调，AABBGGRR 格式（例如 0x99FFFFFF 为半透明白）</param>
    /// <returns>调用成功返回 true；调用方仍需自行判断是否黑屏并降级</returns>
    public static bool TryEnableAcrylic(IntPtr hwnd, uint tintColor)
    {
        if (hwnd == IntPtr.Zero) return false;

        return SetAccent(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, tintColor);
    }

    /// <summary>仅模糊、不叠加 Acrylic 色（部分系统 Acrylic 不可用时的次选）。</summary>
    public static bool TryEnableBlur(IntPtr hwnd)
        => SetAccent(hwnd, ACCENT_ENABLE_BLURBEHIND, 0);

    /// <summary>关闭窗口特效。</summary>
    public static void Disable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetAccent(hwnd, ACCENT_DISABLED, 0);
    }

    private static bool SetAccent(IntPtr hwnd, int accentState, uint tintColor)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = 0,
                GradientColor = unchecked((int)tintColor),
                AnimationId = 0,
            };

            var size = Marshal.SizeOf<AccentPolicy>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = pointer,
                    SizeOfData = size,
                };

                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
