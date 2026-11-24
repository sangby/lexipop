using System.Windows.Input;
using LexiPop.Interop;

namespace LexiPop.Services;

/// <summary>把 "Ctrl+Alt+D" 这类字符串转换成 RegisterHotKey 需要的修饰键和虚拟键。</summary>
public static class HotkeyParser
{
    public static (uint Modifiers, uint VirtualKey)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        uint modifiers = 0;
        uint virtualKey = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    if (Enum.TryParse<Key>(raw, ignoreCase: true, out var key))
                    {
                        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    }
                    break;
            }
        }

        return virtualKey == 0 ? null : (modifiers, virtualKey);
    }
}
