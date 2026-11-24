using System.Windows.Automation;

namespace LexiPop.Services;

/// <summary>
/// 取词第一级：UI Automation 读取当前焦点控件的文本选区，不触碰剪贴板。
/// 失败时返回 null，由上层回退到剪贴板方案（M2 实现）。
/// </summary>
public static class SelectionCapture
{
    private const int MaxLength = 400;

    public static string? TryGetSelectedText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return null;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
            if (pattern is not TextPattern textPattern) return null;

            var ranges = textPattern.GetSelection();
            if (ranges.Length == 0) return null;

            var text = ranges[0].GetText(MaxLength + 1)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;

            // GetText 是截断读取，超长说明是整段/整页被选中，交给上层决定
            if (text.Length > MaxLength) return null;

            return text;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (Exception) { return null; }
    }
}
