// 识别评测工具（M2）：
//   --mode=visual   外观分类评测：逐图运行视觉识别（禁用照片内条码作答），按标签清单统计
//                   Top1/Top3/未匹配/负样本错误接受/混淆矩阵。QR 图片作为输入也不能用编码作答。
//   --mode=qr       扫码回环评测：--qr-gen=<目录> 先按配件库批量生成 PART:code 二维码图，
//                   再逐图解码回环。标题明确“扫码回环”，不计入外观指标。
//   --labels=<csv>  标签清单：image,expectedPartCode（无 BOM 的 UTF-8；# 注释；expected 空=负样本）。
//                   正式评测以人工核对清单为准；可用 --draft=<图片目录> 生成草案清单。
//   --out=<dir>     输出目录（默认 docs/测试记录/识别评测_<时间>）。
//   --conn=<str>    连接串（或 INDUSTRIALREPAIR_TEST_CONN）；评测只读，但必须显式指定。
// 输出：per_image.csv / summary.csv / run.json（模式、清单与特征库哈希、预处理参数、降级计数、原始统计）。
// 缺图/无效标签行/未知正样本编码/空图集：显式报错并以非零码退出，不静默剔除样本。
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

string? Arg(string name) => Array.Find(args, a => a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 3)..];
var mode = Arg("mode") ?? "";
var conn = Arg("conn") ?? Environment.GetEnvironmentVariable("INDUSTRIALREPAIR_TEST_CONN");

if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("× 必须显式指定 --conn=<连接串>（评测只读，但特征库/编码集来源必须明确）。");
    return 2;
}
Db.UseConnectionString(conn);

var outDir = Arg("out");
if (string.IsNullOrWhiteSpace(outDir))
{
    var docs = Path.Combine(Directory.GetCurrentDirectory(), "docs", "测试记录");
    outDir = Path.Combine(docs, $"识别评测_{DateTime.Now:yyyyMMdd_HHmmss}");
}
Directory.CreateDirectory(outDir);

try
{
    return mode.Equals("qr", StringComparison.OrdinalIgnoreCase)
        ? await RunQrAsync(outDir)
        : mode.Equals("visual", StringComparison.OrdinalIgnoreCase)
            ? RunVisual(outDir)
            : Usage("未知 --mode（仅支持 visual / qr）");
}
catch (Exception ex)
{
    Console.Error.WriteLine("× 评测失败：" + ex.Message);
    return 1;
}

static int Usage(string msg)
{
    Console.Error.WriteLine("× " + msg);
    Console.Error.WriteLine("用法示例：dotnet run --project test/RecognizeEval -- --mode=visual --labels=labels.csv --conn=\"...\"");
    Console.Error.WriteLine("          dotnet run --project test/RecognizeEval -- --mode=qr --qr-gen=synthetic_qr --qr-count=6 --conn=\"...\"");
    return 2;
}

/*-------------------- visual：外观分类评测（禁用条码作答）--------------------*/
int RunVisual(string outDir)
{
    var labelsFile = Arg("labels");
    if (string.IsNullOrWhiteSpace(labelsFile)) return Usage("visual 模式必须提供 --labels=<csv>");

    // 标签解析（NR7：CsvHelper 统一读取，支持 # 注释；表头/行格式错误由库带行号报错）
    List<(string Image, string? Expected)> labels;
    try
    {
        labels = CsvIo.ReadLabels(labelsFile);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"× 标签清单解析失败：{ex.Message}");
        return 1;
    }
    if (labels.Count == 0) return Usage("标签清单为空（至少一行有效记录）");

    // 正样本期望编码必须存在于配件库（防止标签笔误抬高指标）
    var known = new HashSet<string>(new PartService().Search(null).Select(p => p.PartCode), StringComparer.OrdinalIgnoreCase);
    var unknown = labels.Where(l => l.Expected != null && !known.Contains(l.Expected!)).ToList();
    if (unknown.Count > 0)
    {
        Console.Error.WriteLine("× 标签含配件库不存在的编码：" + string.Join("、", unknown.Select(u => u.Expected)));
        return 1;
    }

    // 缺图显式报错（不静默剔除）
    var missing = labels.Where(l => !File.Exists(l.Image)).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine("× 标签指向的图片缺失：\n  " + string.Join("\n  ", missing.Select(m => m.Image)));
        return 1;
    }

    var featureHash = FeatureLibraryHash();
    var engine = new RecognitionEngine();
    var rows = new List<EvalRow>();
    int degraded = 0;
    foreach (var (image, expected) in labels)
    {
        var sw = Stopwatch.StartNew();
        var top = engine.Recognize(image, includeBarcode: false);   // 外观评测：照片内条码不得作答
        sw.Stop();
        if (top.Any(r => r.Source == RecognizeSource.Legacy)) degraded++;
        rows.Add(new EvalRow(
            Path.GetFileName(image), expected,
            top.Count > 0 ? top[0].PartCode : null,
            top.Count > 0 ? top[0].Source : null,
            top.Count > 0 ? top[0].Score : 0,
            top.Select(r => r.PartCode).ToList(),
            top.Count == 0 || top[0].Source == RecognizeSource.Unmatched,
            sw.Elapsed.TotalMilliseconds));
    }

    return WriteOutputs(outDir, "visual", labels, rows, featureHash, degraded, includeConfusion: true);
}

/*-------------------- qr：扫码回环评测（独立 synthetic 图集）--------------------*/
async Task<int> RunQrAsync(string outDir)
{
    var qrDir = Arg("qr-gen");
    if (string.IsNullOrWhiteSpace(qrDir)) return Usage("qr 模式必须提供 --qr-gen=<目录>");
    var count = int.TryParse(Arg("qr-count") ?? "6", out var c) && c > 0 ? c : 6;

    // 按配件库前 N 个启用件生成 synthetic_qr 图集（文件名=编码.png）
    Directory.CreateDirectory(qrDir);
    var parts = new PartService().Search(null).Where(p => p.Status).Take(count).ToList();
    if (parts.Count == 0) return Usage("配件库为空，无法生成二维码图集");
    var labels = new List<(string Image, string? Expected)>();
    foreach (var p in parts)
    {
        var file = QrLabelService.Generate(p.PartCode, p.PartName);
        var dest = Path.Combine(qrDir, p.PartCode + ".png");
        File.Copy(file, dest, overwrite: true);
        labels.Add((dest, p.PartCode));
    }
    Console.WriteLine($"▶ 已生成扫码回环图集：{qrDir}（{labels.Count} 张，PART:编码 二维码）");

    var rows = new List<EvalRow>();
    foreach (var (image, expected) in labels)
    {
        var sw = Stopwatch.StartNew();
        var text = BarcodeUtil.Decode(image);
        var code = BarcodeUtil.ExtractPartCode(text);
        sw.Stop();
        rows.Add(new EvalRow(Path.GetFileName(image), expected, code, RecognizeSource.Barcode,
            code == null ? 0 : 100, code == null ? Array.Empty<string>() : new[] { code },
            code == null, sw.Elapsed.TotalMilliseconds));
    }

    Console.WriteLine("ℹ 本结果为“扫码回环”（QRCoder 生成 → ZXing 解码），不代表外观分类效果，不得混入 visual 指标。");
    return WriteOutputs(outDir, "qr", labels, rows, FeatureLibraryHash(), degraded: 0, includeConfusion: true);
}

/*-------------------- 输出 --------------------*/
int WriteOutputs(string outDir, string mode, List<(string Image, string? Expected)> labels,
    List<EvalRow> rows, string featureHash, int degraded, bool includeConfusion)
{
    var stats = RecognitionEvalCore.Compute(rows);

    var perImage = Path.Combine(outDir, "per_image.csv");
    var sb = new StringBuilder();
    sb.Append(RecognitionEvalCore.CsvLine("image", "expected", "top1", "source", "score", "top3", "unmatched", "elapsed_ms"));
    foreach (var r in rows)
        sb.Append(RecognitionEvalCore.CsvLine(r.Image, r.ExpectedPartCode, r.Top1PartCode, r.Top1Source,
            r.Top1Score.ToString(), string.Join(" ", r.Top3Codes), r.Unmatched ? "1" : "0",
            r.ElapsedMs.ToString("F1")));
    File.WriteAllText(perImage, sb.ToString(), new UTF8Encoding(true));

    var summaryFile = Path.Combine(outDir, "summary.csv");
    sb = new StringBuilder();
    sb.Append(RecognitionEvalCore.CsvLine("指标", "数值"));
    foreach (var (metric, value) in RecognitionEvalCore.SummaryRows(stats))
        sb.Append(RecognitionEvalCore.CsvLine(metric, value));
    File.WriteAllText(summaryFile, sb.ToString(), new UTF8Encoding(true));

    var run = new
    {
        mode,
        startedAt = DateTime.Now.ToString("O"),
        title = mode == "qr" ? "扫码回环评测（非外观指标）" : "外观分类评测（禁用照片内条码作答）",
        imageCount = rows.Count,
        positiveCount = stats.Positives,
        negativeCount = stats.Negatives,
        engine = "RecognitionEngineV2(ZXing+OpenCV)",
        preProcess = new { maxEdge = 800, jpegQuality = 75 },
        openCvDegradedImages = degraded,
        featureLibHash = featureHash,
        stats = new
        {
            stats.Positives, stats.Top1Correct, stats.Top3Hit, stats.Unmatched,
            stats.Negatives, stats.NegAccepted
        }
    };
    File.WriteAllText(Path.Combine(outDir, "run.json"),
        JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
        new UTF8Encoding(true));

    Console.WriteLine($"▶ 输出目录：{outDir}");
    foreach (var (metric, value) in RecognitionEvalCore.SummaryRows(stats))
        Console.WriteLine($"  {metric,-12}{value}");
    if (degraded > 0)
        Console.WriteLine($"⚠ {degraded} 张图 OpenCV 降级为规则路径：环境未达标，结果不代表 OpenCV 路径效果。");
    return 0;
}

/*-------------------- 特征库快照哈希（可复现性）--------------------*/
string FeatureLibraryHash()
{
    var sb = new StringBuilder();
    foreach (var p in new PartService().Search(null))
        sb.Append(p.PartCode).Append('|').Append(p.FeatureJson ?? "").Append('\n');
    return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
}
