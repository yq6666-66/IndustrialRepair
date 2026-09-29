using System.Drawing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace IndustrialRepair.BLL;

/// <summary>
/// 条码/二维码解码（开源库 ZXing.Net，~2.8k★，Apache-2.0）。
/// 用于：报修照片含 PART: 编码二维码时直接命中配件；串口页“从图片扫码”。
/// </summary>
public static class BarcodeUtil
{
    private static readonly BarcodeReader Reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions { TryHarder = true, TryInverted = true }
    };

    public static string? Decode(string imageFile)
    {
        if (!File.Exists(imageFile)) return null;
        using var bmp = new Bitmap(imageFile);
        return Decode(bmp);
    }

    public static string? Decode(Bitmap bitmap) =>
        Reader.Decode(bitmap)?.Text;

    /// <summary>从扫码内容提取配件编码（PART:xxx 或裸编码）</summary>
    public static string? ExtractPartCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.StartsWith("PART:", StringComparison.OrdinalIgnoreCase)) return text[5..].Trim();
        return text.Length is (> 3 and < 20) ? text : null;
    }
}
