using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IndustrialRepair.BLL;

/// <summary>
/// 图像工具：报修照片“本地压缩后上传”（任务书：本地拍照压缩上传）。
/// </summary>
public static class ImageUtil
{
    /// <summary>压缩另存：最长边不超过 maxSide，JPEG 质量 quality</summary>
    public static string CompressSave(string srcPath, string destDir, int maxSide = 800, int quality = 75)
    {
        Directory.CreateDirectory(destDir);
        var name = $"IMG_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(srcPath)}.jpg";
        var dest = Path.Combine(destDir, name);

        using var src = Image.FromFile(srcPath);
        int w = src.Width, h = src.Height;
        if (Math.Max(w, h) > maxSide)
        {
            var scale = maxSide / (double)Math.Max(w, h);
            w = Math.Max(1, (int)(w * scale));
            h = Math.Max(1, (int)(h * scale));
        }
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
        }
        var codec = GetJpegCodec();
        var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        bmp.Save(dest, codec, ep);
        return dest;
    }

    private static ImageCodecInfo GetJpegCodec()
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.MimeType == "image/jpeg") return c;
        throw new InvalidOperationException("JPEG 编码器不可用。");
    }

    public static long FileSizeKB(string path) =>
        new FileInfo(path).Exists ? new FileInfo(path).Length / 1024 : 0;
}
