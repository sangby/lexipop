using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace LexiPop.Services;

/// <summary>
/// 一套完整的视觉风格方案：
/// 配色角色 + 明暗标志 + 玻璃质感参数。
/// 组件只引用角色键，不直接写十六进制，换主题时角色值整体替换。
/// </summary>
public sealed record ThemePreset(
    string Id,
    string Name,
    bool IsDark,
    bool IsGlass,          // true 时窗口启用 Acrylic 毛玻璃，背景透明
    string Background,
    string Surface,
    string SurfaceElevated,
    string SurfaceBorder,
    string TextPrimary,
    string TextSecondary,
    string TextMuted,
    string Accent,
    string AccentHover,
    string Success,
    string Warning,
    string Error,
    string StatusBar,
    // ---- 玻璃质感 ----
    string GlassSurface,    // 半透明面板底色（0.6–0.85 不透明度）
    string GlassHighlight,  // 1px 高光描边（半透明白）
    string GlassBorder);    // 半透明常规描边

/// <summary>
/// 主题管理。切换时同步写入 Application.Resources，
/// 所有 {DynamicResource Theme*} 引用在同一帧内失效重绘（通常 &lt;16ms）。
/// </summary>
public static class ThemeService
{
    public static IReadOnlyList<ThemePreset> Presets { get; } =
    [
        new("graphite", "石墨", true, false,
            "#1A1B22", "#22252D", "#23262E", "#343848",
            "#F2F4F8", "#A8ADBB", "#6B7080",
            "#8B9CFF", "#A5B2FF", "#4ADE80", "#FBBF24", "#F87171", "#15161C",
            "#B31F2129", "#26FFFFFF", "#33FFFFFF"),

        new("midnight", "午夜", true, false,
            "#0F1115", "#15181F", "#171A22", "#262B36",
            "#E8EDF5", "#8B94A6", "#565E6E",
            "#4DD8C0", "#6EE8D4", "#34D399", "#FBBF24", "#F87171", "#0A0C10",
            "#B30D0F13", "#26FFFFFF", "#33FFFFFF"),

        new("amber", "暖灰", true, false,
            "#1C1917", "#262220", "#231F1D", "#3A3430",
            "#F5EDE6", "#B0A49A", "#776B62",
            "#E89A5C", "#F2AE75", "#4ADE80", "#FBBF24", "#F87171", "#161312",
            "#B31F1B19", "#26FFFFFF", "#33FFFFFF"),

        new("violet", "紫夜", true, false,
            "#16121F", "#1E182B", "#1C1728", "#322A45",
            "#F0ECF8", "#A79BBF", "#6E6385",
            "#B18CFF", "#C4A6FF", "#4ADE80", "#FBBF24", "#F87171", "#110E19",
            "#B3181422", "#26FFFFFF", "#33FFFFFF"),

        new("light", "浅色", false, false,
            "#E9EBF0", "#FFFFFF", "#FFFFFF", "#DDE1E8",
            "#1A1D24", "#5A6270", "#8A92A0",
            "#3B6CF6", "#5B85F8", "#16A34A", "#D97706", "#DC2626", "#ECEEF2",
            "#D9FFFFFF", "#66FFFFFF", "#33000000"),

        // 玻璃主题：窗口背景透明，由 Acrylic 提供真实 backdrop blur；
        // 采用"浅色磨砂玻璃"（半透明白 + 深色文字），在浅色桌面上对比度最好。
        new("glass", "玻璃", false, true,
            "#00000000", "#80FFFFFF", "#99FFFFFF", "#B3FFFFFF",
            "#14161C", "#3A3F4A", "#6A7080",
            "#3B6CF6", "#5B85F8", "#16A34A", "#D97706", "#DC2626", "#80FFFFFF",
            "#66FFFFFF", "#CCFFFFFF", "#4D000000"),
    ];

    public static ThemePreset Current { get; private set; } = Presets[0];

    public static event Action? Changed;

    /// <summary>最近一次应用耗时（毫秒），用于验收 100ms 指标。</summary>
    public static long LastApplyMilliseconds { get; private set; }

    public static void Apply(string? id)
    {
        var stopwatch = Stopwatch.StartNew();

        var preset = Presets.FirstOrDefault(p => p.Id == id) ?? Presets[0];
        Current = preset;

        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            resources["ThemeBackground"] = Brush(preset.Background);
            resources["ThemeSurface"] = Brush(preset.Surface);
            resources["ThemeSurfaceElevated"] = Brush(preset.SurfaceElevated);
            resources["ThemeBorder"] = Brush(preset.SurfaceBorder);
            resources["ThemeTextPrimary"] = Brush(preset.TextPrimary);
            resources["ThemeTextSecondary"] = Brush(preset.TextSecondary);
            resources["ThemeTextMuted"] = Brush(preset.TextMuted);
            resources["ThemeAccent"] = Brush(preset.Accent);
            resources["ThemeAccentHover"] = Brush(preset.AccentHover);
            resources["ThemeSuccess"] = Brush(preset.Success);
            resources["ThemeWarning"] = Brush(preset.Warning);
            resources["ThemeError"] = Brush(preset.Error);
            resources["ThemeStatusBar"] = Brush(preset.StatusBar);

            resources["ThemeRadiusSmall"] = new CornerRadius(6);
            resources["ThemeRadiusMedium"] = new CornerRadius(10);
            resources["ThemeRadiusLarge"] = new CornerRadius(14);

            resources["ThemeIsDark"] = preset.IsDark;
            resources["ThemeIsGlass"] = preset.IsGlass;

            // 窗口底色：glass 主题用半透明（让桌面透出），其余用不透明主题色
            resources["ThemeWindowSurface"] = Brush(
                preset.IsGlass ? preset.GlassSurface : preset.Background);

            // 玻璃质感：半透明面板 + 高光描边，随主题一起切换
            resources["ThemeGlassSurface"] = Brush(preset.GlassSurface);
            resources["ThemeGlassHighlight"] = Brush(preset.GlassHighlight);
            resources["ThemeGlassBorder"] = Brush(preset.GlassBorder);
        }

        stopwatch.Stop();
        LastApplyMilliseconds = stopwatch.ElapsedMilliseconds;

        App.Log($"theme: applied {preset.Id} in {LastApplyMilliseconds}ms");
        Changed?.Invoke();
    }

    /// <summary>取当前主题的资源画刷，供代码动态生成的元素使用。</summary>
    public static SolidColorBrush Get(string key)
        => (SolidColorBrush)(Application.Current?.TryFindResource(key) ?? Brush("#FFFFFF"));

    public static SolidColorBrush Brush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
