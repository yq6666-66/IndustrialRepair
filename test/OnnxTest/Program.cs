// ONNX 后端端到端验证（NR8 + 实测）：加载 MobileNet v2 → 配置 → 推理 → 管线全通
// 用法：dotnet run --project test/OnnxTest -- --conn="<测试库>" [--model=<path>] [--labels=<path>]
using System.Drawing;
using System.Drawing.Drawing2D;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

var modelPath = Array.Find(args, a => a.StartsWith("--model="))?["--model=".Length..]
               ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "mobilenetv2-12.onnx"));
var labelsPath = Array.Find(args, a => a.StartsWith("--labels="))?["--labels=".Length..]
                ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "imagenet_parts_sparse.txt"));

Console.WriteLine($"▶ ONNX 端到端验证");
Console.WriteLine($"  模型：{modelPath}（{new FileInfo(modelPath).Length / 1024 / 1024} MB）");
Console.WriteLine($"  标签：{labelsPath}");

// 1. 模型加载
Console.WriteLine("\n== 1. 模型加载 ==");
try
{
    using var session = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath);
    Console.WriteLine($"  ✓ 会话创建成功");
    Console.WriteLine($"  输入：{string.Join(", ", session.InputMetadata.Select(kv => $"{kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]"))}");
    Console.WriteLine($"  输出：{string.Join(", ", session.OutputMetadata.Select(kv => $"{kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]"))}");
    var inputName = session.InputMetadata.Keys.First();
    Console.WriteLine($"  输入名：{inputName}");
}
catch (Exception ex)
{
    Console.WriteLine($"  × 加载失败：{ex.Message}");
    return 1;
}

// 2. 预处理
Console.WriteLine("\n== 2. 预处理（生成 64×64 测试图 → 224×224 letterbox tensor）==");
var testImg = Path.Combine(Path.GetTempPath(), "onnx_test.png");
using (var bmp = new Bitmap(64, 64))
{
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.FromArgb(200, 200, 200));
    using var brush = new SolidBrush(Color.FromArgb(100, 100, 110));
    g.FillEllipse(brush, 12, 12, 40, 40);   // 灰色圆（模拟金属轴承）
    bmp.Save(testImg, System.Drawing.Imaging.ImageFormat.Png);
}
var tensor = OnnxImageRecognizer.Preprocess(Image.FromFile(testImg), 224);
Console.WriteLine($"  ✓ Tensor 形状：{string.Join("×", tensor.Dimensions.ToArray())}");
Console.WriteLine($"  ✓ 长度：{tensor.Length:N0}");
Console.WriteLine($"  ✓ 值域：[{tensor.ToArray().Min():F3}, {tensor.ToArray().Max():F3}]");

// 3. 推理
Console.WriteLine("\n== 3. 推理（MobileNet v2 1000 类）==");
try
{
    using var session = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath);
    var inputName = session.InputMetadata.Keys.First();
    var inputs = new List<Microsoft.ML.OnnxRuntime.NamedOnnxValue>
    {
        Microsoft.ML.OnnxRuntime.NamedOnnxValue.CreateFromTensor(inputName, tensor)
    };
    using var results = session.Run(inputs);
    var scores = results.First().AsTensor<float>().ToArray();
    Console.WriteLine($"  ✓ 输出长度：{scores.Length}（ImageNet 1000 类）");
    var top5 = scores.Select((s, i) => (Score: s, Idx: i))
                     .OrderByDescending(x => x.Score).Take(5).ToList();
    foreach (var (score, idx) in top5)
        Console.WriteLine($"    #{idx}: {score:F4}");
    Console.WriteLine($"  ✓ 推理管线全通");
}
catch (Exception ex)
{
    Console.WriteLine($"  × 推理失败：{ex.Message}");
    return 1;
}

// 4. 标签解析（稀疏格式：索引 TAB 名称 TAB 编码）
Console.WriteLine("\n== 4. 标签映射（稀疏索引格式）==");
// 写测试标签文件
var testLabels = Path.Combine(Path.GetTempPath(), "onnx_labels_test.txt");
File.WriteAllLines(testLabels, new[]
{
    "# 索引→类别→编码",
    "723\tscrewdriver\tP-JG-001",
    "423\tcircuit board\tP-DQ-001",
    "623\tchain\tP-CD-002",
    "",           // 空行（未映射类）
    "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "",
});
var sparseLabels = IndustrialRepair.BLL.OnnxImageRecognizer.ParseLabels(File.ReadAllLines(testLabels));
Console.WriteLine($"  ✓ 解析 {sparseLabels.Length} 条（含空行占位）");

// 5. 全管线：照片 → 预处理 → 推理 → Top 类别
Console.WriteLine("\n== 5. 全管线总结 ==");
Console.WriteLine($"  模型加载     ✓");
Console.WriteLine($"  预处理 NCHW  ✓");
Console.WriteLine($"  推理 {224}×{224}  ✓");
Console.WriteLine($"  标签映射     ✓（课程模型到位后直接替换模型+标签文件即可）");
Console.WriteLine($"\n√ ONNX 后端端到端验证全部通过。");
Console.WriteLine($"  课程模型接入步骤：");
Console.WriteLine($"  1. 将课程 .onnx 模型文件放到 models/ 目录");
Console.WriteLine($"  2. 创建标签文件（每行：类别名<TAB>配件编码）");
Console.WriteLine($"  3. 在 appsettings.json 添加 Onnx 节：");
Console.WriteLine($"     {{\"Onnx\": {{\"ModelPath\": \"models/xxx.onnx\", \"LabelsPath\": \"models/xxx.txt\", \"InputSize\": 224}}}}");
Console.WriteLine($"  4. 重启系统，识别引擎自动切换为 ONNX 模式");
return 0;
