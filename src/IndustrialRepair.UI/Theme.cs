using System;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace IndustrialRepair.UI;

/// <summary>主题色板。</summary>
public sealed record ThemePalette(
    System.Drawing.Color PageBg, System.Drawing.Color CardBg, System.Drawing.Color HeaderBar,
    System.Drawing.Color NavBg, System.Drawing.Color NavActive, System.Drawing.Color GridHeader,
    System.Drawing.Color GridRow, System.Drawing.Color GridAltRow, System.Drawing.Color GridSelection,
    System.Drawing.Color TextPrimary, System.Drawing.Color TextSecondary, System.Drawing.Color Accent,
    System.Drawing.Color Danger, System.Drawing.Color Success, System.Drawing.Color Border,
    System.Drawing.Color InputBg, System.Drawing.Color InputBorder, System.Drawing.Color LabelColor,
    System.Drawing.Color PanelBg)
{
    /// <summary>亮色（默认）。</summary>
    public static readonly ThemePalette Light = new(
        PageBg: Color.FromArgb(240, 243, 248),
        CardBg: Color.White,
        HeaderBar: Color.FromArgb(41, 108, 202),
        NavBg: Color.FromArgb(32, 41, 56),
        NavActive: Color.FromArgb(64, 134, 240),
        GridHeader: Color.FromArgb(32, 41, 56),
        GridRow: Color.White,
        GridAltRow: Color.FromArgb(246, 249, 252),
        GridSelection: Color.FromArgb(41, 108, 202),
        TextPrimary: Color.FromArgb(32, 41, 56),
        TextSecondary: Color.FromArgb(110, 122, 138),
        Accent: Color.FromArgb(41, 108, 202),
        Danger: Color.FromArgb(214, 69, 66),
        Success: Color.FromArgb(80, 160, 90),
        Border: Color.FromArgb(226, 232, 240),
        InputBg: Color.White,
        InputBorder: Color.FromArgb(200, 206, 214),
        LabelColor: Color.FromArgb(90, 100, 115),
        PanelBg: Color.FromArgb(243, 246, 250));

    /// <summary>暗色。</summary>
    public static readonly ThemePalette Dark = new(
        PageBg: Color.FromArgb(30, 33, 40),
        CardBg: Color.FromArgb(40, 44, 52),
        HeaderBar: Color.FromArgb(35, 42, 58),
        NavBg: Color.FromArgb(22, 25, 32),
        NavActive: Color.FromArgb(60, 120, 220),
        GridHeader: Color.FromArgb(22, 25, 32),
        GridRow: Color.FromArgb(40, 44, 52),
        GridAltRow: Color.FromArgb(35, 38, 46),
        GridSelection: Color.FromArgb(60, 120, 220),
        TextPrimary: Color.FromArgb(220, 224, 230),
        TextSecondary: Color.FromArgb(140, 150, 165),
        Accent: Color.FromArgb(80, 150, 255),
        Danger: Color.FromArgb(255, 107, 107),
        Success: Color.FromArgb(105, 200, 130),
        Border: Color.FromArgb(55, 60, 70),
        InputBg: Color.FromArgb(35, 38, 46),
        InputBorder: Color.FromArgb(60, 65, 75),
        LabelColor: Color.FromArgb(160, 170, 185),
        PanelBg: Color.FromArgb(35, 38, 46));
}

/// <summary>
/// 全局主题管理器（OR1）：暗/亮切换 + 偏好持久化（%LocalAppData%\IndustrialRepair\ui.json 的 "theme" 节）。
/// 切换后所有已创建窗体实时着色（广播事件，各控件自行响应）。
/// </summary>
internal static class Theme
{
    public static ThemePalette Current { get; private set; } = ThemePalette.Light;
    public static bool IsDark => Current == ThemePalette.Dark;

    /// <summary>主题切换广播（窗体/页面订阅后重着色）。</summary>
    public static event Action? Changed;

    private static readonly string PrefFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IndustrialRepair", "ui.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(PrefFile)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(PrefFile));
            if (doc.RootElement.TryGetProperty("theme", out var t) && t.GetString() == "dark")
                Current = ThemePalette.Dark;
        }
        catch { }
    }

    public static void Toggle()
    {
        Current = IsDark ? ThemePalette.Light : ThemePalette.Dark;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefFile)!);
            var existing = File.Exists(PrefFile) ? File.ReadAllText(PrefFile) : "{}";
            using var doc = JsonDocument.Parse(existing);
            var writer = new Utf8JsonWriter(File.Create(PrefFile));
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Name != "theme") prop.WriteTo(writer);
            writer.WriteString("theme", IsDark ? "dark" : "light");
            writer.WriteEndObject();
            writer.Dispose();
        }
        catch { }
    }
}
