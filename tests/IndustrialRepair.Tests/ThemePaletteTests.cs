using System.Drawing;
using System.Reflection;
using IndustrialRepair.UI;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>深灰电光蓝色板的完整性与可读性（WCAG 对比度）守护。</summary>
public class ThemePaletteTests
{
    private static readonly ThemePalette P = ThemePalette.Dark;

    [Fact]
    public void Current_IsDarkPalette()
        => Assert.Equal(ThemePalette.Dark, Theme.Current);

    [Fact]
    public void Every_token_is_opaque()
    {
        foreach (var prop in typeof(ThemePalette).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.PropertyType == typeof(Color)))
        {
            var c = (Color)prop.GetValue(P)!;
            Assert.True(c.A == 255, $"令牌 {prop.Name} 不是不透明颜色");
        }
    }

    [Theory]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.PageBg))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.CardBg))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.HeaderBar))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.NavBg))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.GridRow))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.GridAltRow))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.GridHeader))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.InputBg))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.PanelBg))]
    [InlineData(nameof(ThemePalette.TextPrimary), nameof(ThemePalette.GridSelection))]
    [InlineData(nameof(ThemePalette.LabelColor), nameof(ThemePalette.CardBg))]
    [InlineData(nameof(ThemePalette.LabelColor), nameof(ThemePalette.PageBg))]
    [InlineData(nameof(ThemePalette.TextSecondary), nameof(ThemePalette.CardBg))]
    [InlineData(nameof(ThemePalette.TextSecondary), nameof(ThemePalette.PageBg))]
    public void Text_tokens_meet_AA_normal_text(string textName, string bgName)
    {
        var text = ColorOf(textName);
        var bg = ColorOf(bgName);
        Assert.True(Theme.ContrastRatio(text, bg) >= 4.5,
            $"{textName} 在 {bgName} 上对比度 {Theme.ContrastRatio(text, bg):F2} 低于 4.5");
    }

    [Fact]
    public void White_text_on_primary_button_meets_AA()
        => Assert.True(Theme.ContrastRatio(Color.White, P.Accent) >= 4.5,
            $"主按钮白字对比度 {Theme.ContrastRatio(Color.White, P.Accent):F2} 低于 4.5");

    [Fact]
    public void Accent_stays_visible_as_non_text_on_card()
        => Assert.True(Theme.ContrastRatio(P.Accent, P.CardBg) >= 3.0,
            $"电光蓝在卡片上对比度 {Theme.ContrastRatio(P.Accent, P.CardBg):F2} 低于 3.0（非文字图形）");

    [Fact]
    public void Status_colors_are_distinct_from_card_background()
    {
        foreach (var c in new[] { P.Success, P.Warning, P.Danger, P.Violet, P.Cyan })
            Assert.True(Theme.ContrastRatio(c, P.CardBg) >= 3.0);
    }

    [Fact]
    public void ContrastRatio_is_symmetric_and_bounded()
    {
        Assert.Equal(21.0, Theme.ContrastRatio(Color.White, Color.Black), 2);
        Assert.Equal(Theme.ContrastRatio(P.Accent, P.CardBg), Theme.ContrastRatio(P.CardBg, P.Accent), 6);
        Assert.Equal(1.0, Theme.ContrastRatio(P.CardBg, P.CardBg), 6);
    }

    private static Color ColorOf(string tokenName)
        => (Color)typeof(ThemePalette).GetProperty(tokenName)!.GetValue(P)!;
}
