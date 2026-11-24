using System.Windows;
using LexiPop.Interop;

namespace LexiPop.Services;

/// <summary>
/// 取词第二级：模拟 Ctrl+C 后读剪贴板。
/// UIA 失败时兜底，几乎对所有应用有效；代价是会改写剪贴板内容。
/// </summary>
public static class ClipboardCapture
{
    private const int TimeoutMilliseconds = 500;
    private const int PollIntervalMilliseconds = 20;

    public static string? TryCapture()
    {
        var sequence = NativeMethods.GetClipboardSequenceNumber();

        NativeMethods.SendCtrlC();

        var deadline = Environment.TickCount64 + TimeoutMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(PollIntervalMilliseconds);

            if (NativeMethods.GetClipboardSequenceNumber() == sequence) continue;

            try
            {
                var text = Clipboard.GetText()?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
