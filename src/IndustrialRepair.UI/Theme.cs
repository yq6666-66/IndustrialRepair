using System;
using System.Drawing;

namespace IndustrialRepair.UI;

/// <summary>深灰电光蓝主题色板（v1.8 单一深色主题）。所有界面颜色均从此处取值。</summary>
public sealed record ThemePalette(
    Color PageBg, Color CardBg, Color HeaderBar, Color NavBg, Color NavActive,
    Color GridHeader, Color GridRow, Color GridAltRow, Color GridSelection,
    Color TextPrimary, Color TextSecondary, Color LabelColor,
    Color Accent, Color Glow, Color Cyan,
    Color Success, Color Warning, Color Danger, Color Violet,
    Color Border, Color InputBg, Color InputBorder, Color PanelBg,
    Color DangerTint, Color WarningTint)
{
    public static readonly ThemePalette Dark = new(
        PageBg: Color.FromArgb(22, 25, 31),
        CardBg: Color.FromArgb(30, 34, 41),
        HeaderBar: Color.FromArgb(26, 30, 37),
        NavBg: Color.FromArgb(18, 21, 27),
        NavActive: Color.FromArgb(30, 48, 78),
        GridHeader: Color.FromArgb(36, 41, 50),
        GridRow: Color.FromArgb(30, 34, 41),
        GridAltRow: Color.FromArgb(34, 39, 47),
        GridSelection: Color.FromArgb(28, 78, 150),
        TextPrimary: Color.FromArgb(226, 232, 240),
        TextSecondary: Color.FromArgb(140, 150, 165),
        LabelColor: Color.FromArgb(170, 180, 195),
        Accent: Color.FromArgb(24, 112, 240),
        Glow: Color.FromArgb(0, 200, 255),
        Cyan: Color.FromArgb(40, 220, 230),
        Success: Color.FromArgb(46, 204, 113),
        Warning: Color.FromArgb(255, 184, 0),
        Danger: Color.FromArgb(255, 90, 95),
        Violet: Color.FromArgb(160, 120, 255),
        Border: Color.FromArgb(48, 55, 66),
        InputBg: Color.FromArgb(24, 28, 35),
        InputBorder: Color.FromArgb(60, 68, 80),
        PanelBg: Color.FromArgb(26, 30, 37),
        DangerTint: Color.FromArgb(62, 38, 44),
        WarningTint: Color.FromArgb(62, 54, 28));
}

/// <summary>主题入口：当前色板 + 对比度计算（供界面与单测共用）。</summary>
public static class Theme
{
    public static ThemePalette Current { get; } = ThemePalette.Dark;

    /// <summary>WCAG 2.x 对比度（1..21）。</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Lin(double v)
        {
            v /= 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
}
