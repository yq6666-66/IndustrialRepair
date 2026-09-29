using System.Drawing;
using System.Drawing.Imaging;
using QRCoder;

namespace IndustrialRepair.BLL;

/// <summary>
/// 配件二维码标签生成（开源库 QRCoder，~3.7k★，MIT）。
/// 生成内容约定：PART:配件编码 —— 与扫码枪/模拟器/ZXing 解码协议一致，构成闭环。
/// </summary>
public static class QrLabelService
{
    public static string QrDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IndustrialRepair", "qr");

    /// <summary>生成单个配件二维码标签 PNG（含编码文字），返回文件路径</summary>
    public static string Generate(string partCode, string partName, int pixelsPerModule = 8)
    {
        Directory.CreateDirectory(QrDir);
        var file = Path.Combine(QrDir, $"QR_{partCode}.png");
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode($"PART:{partCode}", QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCode(data);
        const int label = 34;   // 底部标签区高度
        using var bmp = qr.GetGraphic(pixelsPerModule, Color.Black, Color.White, true);
        using var canvas = new Bitmap(bmp.Width, bmp.Height + label);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.DrawImage(bmp, 0, 0);
            using var font = new Font("微软雅黑", 11, FontStyle.Bold);
            var text = $"{partCode}  {partName}";
            var size = g.MeasureString(text, font);
            using var brush = new SolidBrush(Color.Black);
            g.DrawString(text, font, brush, (canvas.Width - size.Width) / 2, bmp.Height + (label - size.Height) / 2);
        }
        canvas.Save(file, ImageFormat.Png);
        return file;
    }

    /// <summary>批量生成（返回文件列表），供仓库整批打印标签</summary>
    public static List<string> GenerateBatch(IEnumerable<(string code, string name)> parts)
    {
        var list = new List<string>();
        foreach (var (code, name) in parts) list.Add(Generate(code, name));
        return list;
    }
}
