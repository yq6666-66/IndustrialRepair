// 无界面端到端冒烟测试：初始化 → 认证 → 工单全流程（会话切换）→ 算料/出库 → 审计与并发 → 大屏/报表/串口/MQTT。
// 运行：dotnet run --project test/SmokeConsole -- --conn="Server=(localdb)\MSSQLLocalDB;Database=IndustrialRepair_Test_R4;Trusted_Connection=True;TrustServerCertificate=True"
// 或设置环境变量 INDUSTRIALREPAIR_TEST_CONN。测试会建号/建单/扣库存——只允许在独立测试库运行。
using System;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

// ---- 0. 测试库隔离：未显式指定连接串或指向业务库时拒绝启动 ----
var connArg = Array.Find(args, a => a.StartsWith("--conn=", StringComparison.OrdinalIgnoreCase));
var testConn = connArg?["--conn=".Length..]
               ?? Environment.GetEnvironmentVariable("INDUSTRIALREPAIR_TEST_CONN");

// --ensure-structure：只做结构检查/升级（无写入型测试），可用于业务库升级演练
if (Array.Exists(args, a => a.Equals("--ensure-structure", StringComparison.OrdinalIgnoreCase)))
{
    if (string.IsNullOrWhiteSpace(testConn))
    {
        Console.Error.WriteLine("× 用法：--ensure-structure --conn=\"<连接串>\"");
        return 2;
    }
    Db.UseConnectionString(testConn);
    try
    {
        new DbInitializer().EnsureStructure();
        Console.WriteLine("√ 结构检查/升级完成。");
        var v = DbHelper.ExecuteScalar("SELECT MAX(Version) FROM dbo.schema_version");
        Console.WriteLine($"  当前结构版本：{v}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("× 结构检查/升级失败：" + ex.Message);
        return 1;
    }
}

// --diag-script=<file>：逐批执行托管预处理后的脚本，定位失败批次（调试用）
var diag = Array.Find(args, a => a.StartsWith("--diag-script=", StringComparison.OrdinalIgnoreCase));
if (diag != null)
{
    if (string.IsNullOrWhiteSpace(testConn)) { Console.Error.WriteLine("× 需要 --conn"); return 2; }
    Db.UseConnectionString(testConn);
    var file = diag["--diag-script=".Length..];
    var batches = ScriptRunner.SplitBatches(ScriptRunner.PrepareManagedScript(File.ReadAllText(file)));
    using var dconn = new SqlConnection(testConn);
    dconn.Open();
    for (int i = 0; i < batches.Count; i++)
    {
        if (string.IsNullOrWhiteSpace(batches[i])) continue;
        try
        {
            using var cmd = new SqlCommand(batches[i], dconn);
            cmd.ExecuteNonQuery();
            Console.WriteLine($"[batch {i + 1}] OK ({batches[i].Length} chars)");
        }
        catch (SqlException ex)
        {
            var lines = batches[i].Split('\n');
            var at = Math.Clamp(ex.LineNumber - 1, 0, lines.Length - 1);
            Console.WriteLine($"[batch {i + 1}] FAIL 行{ex.LineNumber}: {ex.Message}\n   源行：{lines[at].Trim()}");
            return 1;
        }
        catch (Exception ex)
        {
            var lines = batches[i].Split('\n');
            var head = string.Join(' ', lines[..Math.Min(3, lines.Length)]).Trim();
            Console.WriteLine($"[batch {i + 1}] FAIL: {ex.Message}\n   首行：{head}");
            return 1;
        }
    }
    Console.WriteLine("√ 全部批次通过");
    return 0;
}

if (string.IsNullOrWhiteSpace(testConn))
{
    Console.Error.WriteLine("× 拒绝运行：冒烟测试包含写操作，必须显式指定独立测试库连接串。");
    Console.Error.WriteLine("  用法：--conn=\"Server=(localdb)\\MSSQLLocalDB;Database=IndustrialRepair_Test_R4;Trusted_Connection=True;TrustServerCertificate=True\"");
    Console.Error.WriteLine("  或环境变量 INDUSTRIALREPAIR_TEST_CONN。");
    return 2;
}
var dbName = System.Text.RegularExpressions.Regex.Match(testConn,
    @"(?:Database|Initial Catalog)\s*=\s*([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.Trim();
if (string.IsNullOrWhiteSpace(dbName) || dbName.Equals("IndustrialRepairDb", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"× 拒绝运行：目标库 {dbName ?? "(未指定)"} 是业务库，冒烟测试只允许在独立测试库执行。");
    return 2;
}
Db.UseConnectionString(testConn);
Console.WriteLine($"▶ 冒烟测试库：{dbName}");

// 库污染守卫：冒烟假定干净库（约定每次 DROP 重建）；残留大量业务数据时快速失败
int existingOrders = 0;
try
{
    new DbInitializer().EnsureStructure();
    existingOrders = Convert.ToInt32(IndustrialRepair.DAL.DbHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.work_order") ?? 0);
}
catch (Exception ex)
{
    Console.Error.WriteLine("× 结构初始化失败：" + ex.Message);
    return 1;
}
if (existingOrders > 20)
{
    Console.Error.WriteLine($"× 拒绝运行：库内已有 {existingOrders} 张工单（非干净库）。");
    Console.Error.WriteLine("  冒烟假定每次 DROP 重建：sqlcmd -Q \"DROP DATABASE " + dbName + "\" 后重跑；");
    Console.Error.WriteLine("  压测数据请用 test/SeedDemoData 建独立库。");
    return 2;
}

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {name}{(ok || detail == "" ? "" : " —— " + detail)}");
    if (ok) pass++; else fail++;
}

Console.WriteLine("== 1. 数据库初始化与结构升级（版本化迁移） ==");
var initLog = new DbInitializer().EnsureAll();
Console.WriteLine(initLog);
Check("数据库就绪", new BasicRepository().IsDatabaseReady());
Check("结构版本≥2（审阅列/唯一索引/过程齐备）", new DbInitializer().IsStructureReady());
Check("重复启动幂等（EnsureStructure 再跑不报错）", RunGuarded(() => new DbInitializer().EnsureStructure()));

bool RunGuarded(Action a)
{
    try { a(); return true; }
    catch (Exception ex) { Console.WriteLine("   " + ex.Message); return false; }
}

Console.WriteLine("== 2. 认证与安全 ==");
var auth = new AuthService();
var bad = auth.Login("admin", "wrong-password");
Check("错误密码被拒绝", !bad.Success, bad.Message);
var okLogin = auth.Login("admin", "123456");
Check("admin 登录成功(bcrypt)", okLogin.Success, okLogin.Message);
Check("admin 拥有 user.manage 权限(RBAC)", okLogin.Data?.HasPerm("user.manage") == true);
var rep = auth.Login("reporter", "123456");
Check("reporter 登录成功", rep.Success, rep.Message);
Check("reporter 无 user.manage 权限", rep.Data?.HasPerm("user.manage") != true);
var reg = auth.Register("stu_test_" + DateTime.Now.Ticks % 10000, "test1234", "测试学生", "13800000000", "stu@test.com");
Check("注册新账号", reg.Success, reg.Message);
var weakPwd = auth.Register("stu_weak_" + DateTime.Now.Ticks % 10000, "abc123", "弱口令", null, null);
Check("弱口令注册被拒(R5)", !weakPwd.Success && weakPwd.Message.Contains("8"), weakPwd.Message);

Console.WriteLine("== 3. 工单全流程（报修人报修 → 管理员派单 → 工程师接单） ==");
var orderSvc = new WorkOrderService();
var devices = orderSvc.Devices();
Check("设备台账加载", devices.Count > 0);
var reporter = rep.Data!;

// 报修用的现场照片 + 识别候选（候选由界面预览传入，报修不再重复识别）
var testImg = Path.Combine(Path.GetTempPath(), "smoke_part.png");
using (var bmp = new System.Drawing.Bitmap(300, 300))
{
    using var g = System.Drawing.Graphics.FromImage(bmp);
    g.Clear(System.Drawing.Color.FromArgb(235, 235, 238));
    using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(125, 125, 130));
    g.FillEllipse(brush, 60, 60, 180, 180);
    bmp.Save(testImg, System.Drawing.Imaging.ImageFormat.Png);
}
var fakeCandidates = new List<RecognizeResult>
{
    new() { PartId = 1, PartCode = "P-ZC-001", PartName = "深沟球轴承6205", Score = 88, Reason = "smoke:top1" },
    new() { PartId = 4, PartCode = "P-MF-001", PartName = "骨架油封TC40x62x8", Score = 76, Reason = "smoke:top2" }
};
var report = orderSvc.Report(devices[6].DeviceId, reporter.UserId, "冒烟：输送带异响", "机械", PriorityCode.Normal, testImg, fakeCandidates);
Check("创建报修工单（带照片与候选）", report.Success, report.Message);
int orderId = int.Parse(report.Data!);
Check("识别候选 JSON 落库并回读（Top2）", orderSvc.GetCandidates(orderId).Count == 2,
      $"回读 {orderSvc.GetCandidates(orderId).Count} 项");

// 3.1 越权校验：reporter 无 order.assign，派单必须被拒（服务层守卫）
var denied = orderSvc.Assign(orderId, 5, reporter.UserId);
Check("无权限派单被服务层拒绝", !denied.Success, denied.Message);

auth.Login("admin", "123456");   // 切换会话：管理员派单
var engineers = new UserRepository().ListByRole("Engineer");
Check("工程师账号存在", engineers.Count > 0);
var engineer = engineers[0];
Check("派单", orderSvc.Assign(orderId, engineer.UserId, AppSession.Current!.UserId).Success);

// 3.2 越权校验：库管员（warehouse）无 order.handle，接单必须被拒
auth.Login("warehouse", "123456");
var deniedTake = orderSvc.Take(orderId, AppSession.Current!.UserId);
Check("库管员接单被服务层拒绝", !deniedTake.Success, deniedTake.Message);

auth.Login("engineer", "123456");   // 切换会话：获派工程师接单
var engUser = AppSession.Current!;
Console.WriteLine($"   工程师会话 UserId={engUser.UserId}，获派 EngineerId={engineer.UserId}");
var takeR = orderSvc.Take(orderId, engUser.UserId);
Check("接单", takeR.Success, takeR.Message);

// 3.3 触发器审计：非状态更新不得产生流转记录；Create 只记一次
int recBefore = orderSvc.Records(orderId).Count;
DbHelper.ExecuteNonQuery("UPDATE dbo.work_order SET Priority = N'Urgent' WHERE OrderId = @Id",
    CommandType.Text, DbHelper.P("@Id", orderId));
int recAfter = orderSvc.Records(orderId).Count;
Check("非状态更新不产生流转记录", recAfter == recBefore, $"更新前后 {recBefore}→{recAfter}");
Check("Create 流转记录仅一条", orderSvc.Records(orderId).Count(r => r.Action == "Create") == 1);

Console.WriteLine("== 4. 材料审阅与智能算料（M1 全场景） ==");
var pick = new PickingService();
var bomT = DbHelper.GetTable("SELECT PartId, QtyPer FROM dbo.bom WHERE DeviceId=@D",
    CommandType.Text, DbHelper.P("@D", devices[6].DeviceId));
var partInBom = (int)bomT.Rows[0]["PartId"];
var partNotInBom = Convert.ToInt32(DbHelper.ExecuteScalar(
    "SELECT TOP 1 p.PartId FROM dbo.part p WHERE p.Status = 1 AND NOT EXISTS(SELECT 1 FROM dbo.bom b WHERE b.DeviceId = @D AND b.PartId = p.PartId)",
    CommandType.Text, DbHelper.P("@D", devices[6].DeviceId))!);

// 4.1 未审阅直接算料被拒
var noReview = pick.Generate(orderId, engUser.UserName, 1.10m);
Check("未审阅生成领料被拒", !noReview.Success && noReview.Message.Contains("审阅"), noReview.Message);

// 4.2 reporter 无权审阅（服务层权限守卫）
auth.Login("reporter", "123456");
var deniedReview = orderSvc.ReviewMaterials(new MaterialReviewRequest(orderId, MaterialReviewMode.BomOnly, null, null, "越权尝试"));
Check("reporter 材料审阅被拒(权限)", !deniedReview.Success, deniedReview.Message);

// 4.3 工程师对未获派工单算料被拒（管理员代建一张不派给他）
auth.Login("admin", "123456");
var adminReport = orderSvc.Report(devices[7].DeviceId, AppSession.Current!.UserId, "冒烟：管理员代报（未派）", "机械", PriorityCode.Normal, null);
auth.Login("engineer", "123456");
var deniedGen = int.TryParse(adminReport.Data, out var otherOrderId)
    ? pick.Generate(otherOrderId, engUser.UserName, 1.10m)
    : Result<int>.Fail(adminReport.Message);
Check("工程师对未获派工单算料被拒", !deniedGen.Success && deniedGen.Message.Contains("获派"), deniedGen.Message);

// 4.4 BomOnly 审阅 → 整机 BOM 生成
var rvBom = orderSvc.ReviewMaterials(new MaterialReviewRequest(orderId, MaterialReviewMode.BomOnly, null, null, "冒烟：整机 BOM 即可"));
Check("BomOnly 材料审阅", rvBom.Success, rvBom.Message);
var gen = pick.Generate(orderId, engUser.UserName, 1.10m);
Check("BOM×1.10 生成领料单", gen.Success, gen.Message);
string? bomSnapshot = null;   // 历史快照基线：后续审阅变化不得改动已出库单
if (gen.Success)
{
    Check("未出库时重复算料被拦截", !pick.Generate(orderId, engUser.UserName, 1.10m).Success, "应阻止重复生成");
    var items = pick.Items(gen.Data);
    Check("领料明细非空", items.Count > 0, $"共 {items.Count} 项");
    Console.WriteLine("   明细：" + string.Join("；", items.Select(i => $"{i.PartName}×{i.Qty}={i.Amount:F2}")));

    // C# 算料纯函数 ↔ 存储过程 一致性（整机 BOM 语义）
    var expected = BomCalculator.Calculate(
        bomT.AsEnumerable().Select(r => new BomRule((int)r["PartId"], (decimal)r["QtyPer"])), 1.10m);
    var actualQty = items.GroupBy(i => i.PartId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
    bool parity = expected.Count == actualQty.Count &&
                  expected.All(e => actualQty.TryGetValue(e.PartId, out var q) && q == (int)e.Qty);
    Check("C#算料与存储过程数量一致(整机BOM)", parity, $"BOM {expected.Count} 项");

    var snap = DbHelper.ExecuteScalar("SELECT ReviewSnapshot FROM dbo.picking_list WHERE PickingId=@Id",
        CommandType.Text, DbHelper.P("@Id", gen.Data))?.ToString();
    Check("领料单保存审阅快照", !string.IsNullOrEmpty(snap) && snap.Contains("BomOnly"), snap ?? "");
    bomSnapshot = snap;   // 历史快照基线

    // 4.5 Pending 单存在时禁止更改审阅
    var lockRv = orderSvc.ReviewMaterials(new MaterialReviewRequest(orderId, MaterialReviewMode.BomOnly, null, null, "尝试更改"));
    Check("存在未出库单时禁止更改审阅", !lockRv.Success && lockRv.Message.Contains("未出库"), lockRv.Message);

    // 4.6 并发出库：两个线程同单出库，只允许一次成功
    auth.Login("warehouse", "123456");
    var whUser = AppSession.Current!;
    var t1 = Task.Run(() => pick.Issue(gen.Data, whUser.UserName));
    var t2 = Task.Run(() => pick.Issue(gen.Data, whUser.UserName));
    var r1 = await t1; var r2 = await t2;
    Check("并发出库恰好一次成功", r1.Success ^ r2.Success,
        $"#{r1.Success} #{r2.Success}：{(r1.Success ? r2.Message : r1.Message)}");
    Check("出库后状态为 Issued", pick.List(null, null).AsEnumerable()
        .Any(r => Convert.ToInt32(r["PickingId"]) == gen.Data && r["Status"].ToString() == "Issued"));
}

// 4.7 出库后改审阅：BOM 内确认 → 该行 ★重点标记（数量仍按 BOM）
auth.Login("engineer", "123456");
var rvIn = orderSvc.ReviewMaterials(new MaterialReviewRequest(orderId, MaterialReviewMode.PartConfirmed, partInBom, null, "确认重点（BOM内）"));
Check("BOM内重点审阅", rvIn.Success, rvIn.Message);
auth.Login("warehouse", "123456");
var gen2 = pick.Generate(orderId, AppSession.Current!.UserName, 1.10m);
Check("二单生成(BOM内★重点)", gen2.Success, gen2.Message);
var starItem = gen2.Success ? pick.Items(gen2.Data).FirstOrDefault(i => i.PartId == partInBom) : null;
Check("BOM内确认行带★重点标记", starItem != null && (starItem.Remark ?? "").Contains("★"), starItem?.Remark ?? "（行不存在）");
Check("二单出库", pick.Issue(gen2.Data, AppSession.Current!.UserName).Success);

// 4.8 BOM 外补充件：缺用量被拒 → 人工用量按冗余追加
auth.Login("engineer", "123456");
var noQty = orderSvc.ReviewMaterials(new MaterialReviewRequest(orderId, MaterialReviewMode.PartConfirmed, partNotInBom, null, "BOM外缺用量"));
Check("BOM外缺人工用量被拒", !noQty.Success && noQty.Message.Contains("人工基础用量"), noQty.Message);
var rvOut = orderSvc.ReviewMaterials(new MaterialReviewRequest(orderId, MaterialReviewMode.PartConfirmed, partNotInBom, 1.5m, "现场追加补充件"));
Check("BOM外人工用量审阅", rvOut.Success, rvOut.Message);
auth.Login("warehouse", "123456");
var gen3 = pick.Generate(orderId, AppSession.Current!.UserName, 1.10m);
Check("三单生成(BOM外追加)", gen3.Success, gen3.Message);
var extra = gen3.Success ? pick.Items(gen3.Data).FirstOrDefault(i => i.PartId == partNotInBom) : null;
Check("BOM外追加行 Qty=CEILING(1.5×1.10)=2", extra != null && extra.Qty == 2 && (extra.Remark ?? "").Contains("BOM外"),
      $"Qty={extra?.Qty.ToString() ?? "缺失"} {extra?.Remark}");

// 4.9 审阅留痕 + 历史快照不变性（后续审阅变化不回写已出库单）
Check("审阅流转记录≥3条(Review)", orderSvc.Records(orderId).Count(r => r.Action == "Review") >= 3);
var snapStill = DbHelper.ExecuteScalar("SELECT ReviewSnapshot FROM dbo.picking_list WHERE PickingId=@Id",
    CommandType.Text, DbHelper.P("@Id", gen.Data))?.ToString();
Check("已出库单审阅快照保持不变", snapStill == bomSnapshot, "历史清单可解释（快照不随审阅变化）");

Console.WriteLine("== 5. 完工与报表/大屏（工程师会话） ==");
auth.Login("engineer", "123456");
var done = orderSvc.Complete(orderId, AppSession.Current!.UserId, "冒烟测试完工：更换托辊轴承");
Check("完工登记", done.Success, done.Message);
var records = orderSvc.Records(orderId);
Check("流转记录完整(创建→派单→接单→完工)", records.Count >= 4, string.Join("→", records.Select(r => WorkOrderRecord.ToChinese(r.Action))));

// 5.1 分页（R1）：总数一致、页大小约束、页间无重叠
int allOrders = Convert.ToInt32(DbHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.work_order") ?? 0);
var p1 = PagedQuery.WorkOrders(null, null, false, 0, 1, 5);
var p2 = PagedQuery.WorkOrders(null, null, false, 0, 2, 5);
Check("工单分页总数与库内一致", p1.TotalCount == allOrders, $"分页 {p1.TotalCount} / 库内 {allOrders}");
Check("分页页大小约束(每页5条)", p1.Items.Rows.Count <= 5 && p1.Items.Rows.Count > 0, $"第1页 {p1.Items.Rows.Count} 行");
var ids1 = p1.Items.AsEnumerable().Select(r => Convert.ToInt32(r["OrderId"])).ToHashSet();
var ids2 = p2.Items.AsEnumerable().Select(r => Convert.ToInt32(r["OrderId"])).ToHashSet();
Check("分页页间无重叠", !ids1.Overlaps(ids2) && (allOrders <= 5 || ids2.Count > 0), $"p1[{ids1.Count}] p2[{ids2.Count}]（库内 {allOrders} 条）");
var pp = PagedQuery.Parts(null, null, 1, 3);
Check("配件分页可用", pp.TotalCount >= 18 && pp.Items.Rows.Count <= 3, $"配件总数 {pp.TotalCount}");
var dash = new DashboardService();
var (today, pending, working, doneCnt, cost, stockValue) = dash.Summary();
Console.WriteLine($"   大屏汇总：今日{today} 待派{pending} 维修中{working} 已完成{doneCnt} 本月成本{cost} 库存价值{stockValue:N0}");
Check("大屏数据可用", dash.Rows().Count > 0);
Check("大屏含近7日成本系列(R3)", dash.Rows().Any(r => r.Kind == "近7日成本"));
Check("大屏含类别库存系列(R3)", dash.Rows().Any(r => r.Kind == "类别库存"));
Check("高成本工单榜可用(R3)", dash.OrderCostTop(10).Rows.Count >= 1);
Check("维修报表数据可用", orderSvc.ReportSheet().Rows.Count > 0);

Console.WriteLine("== 6. 库存预警与流水 ==");
var partSvc = new PartService();
Check("库存流水已记录", partSvc.StockLogs(0).Rows.Count > 0);

Console.WriteLine("== 7. 串口协议解析 ==");
Check("扫码帧解析", SerialPortService.ParsePartCode("PART:P-ZC-001") == "P-ZC-001");
Check("非法帧忽略", SerialPortService.ParsePartCode("HELLO") == null);

Console.WriteLine("== 8. 会话超时机制 ==");
Check("会话心跳正常", AppSession.Touch());

Console.WriteLine("== 9. 开源组件集成 ==");
// 9.1 QRCoder 生成 → ZXing 解码 回环
var qrFile = QrLabelService.Generate("P-ZC-001", "深沟球轴承6205");
Check("QRCoder 生成二维码标签", File.Exists(qrFile) && new FileInfo(qrFile).Length > 0, qrFile);
var decoded = BarcodeUtil.Decode(qrFile);
Check("ZXing 解码回环命中", decoded == "PART:P-ZC-001", $"解码结果：{decoded}");
Check("扫码内容提取配件编码", BarcodeUtil.ExtractPartCode(decoded) == "P-ZC-001");

// 9.2 OpenCV 识别引擎 V2（复用第 3 节生成的灰色圆形零件测试图）
var recResults = new RecognitionEngine().Recognize(testImg);
Check("识别引擎V2 返回候选", recResults.Count > 0 && recResults[0].Score > 0,
      $"Top1 {recResults[0].PartName} {recResults[0].Score}%：{recResults[0].Reason}");
Check("OpenCV 原生库可用（圆度/轮廓路径）", recResults.Any(r => r.Reason.Contains("OpenCV")),
      "若降级为[规则]路径请检查 OpenCvSharp4.runtime.win");

// 9.3 ClosedXML 导出
var xlsTable = new DataTable();
xlsTable.Columns.Add("编码", typeof(string));
xlsTable.Columns.Add("金额", typeof(decimal));
xlsTable.Rows.Add("P-ZC-001", 55.50m);
xlsTable.Rows.Add("P-RH-001", 84.00m);
var xlsFile = ExcelService.ExportTable(xlsTable, "冒烟导出", "Sheet1", totalRow: true);
Check("ClosedXML 导出 .xlsx", File.Exists(xlsFile) && new FileInfo(xlsFile).Length > 4000, xlsFile);

// 9.4 MQTTnet 内嵌 Broker：确定性越限 → 自动报修；冷却窗内重复告警不重复建单
Console.WriteLine("== 10. MQTT 确定性越限联动 ==");
try
{
    int iotCount() => Convert.ToInt32(DbHelper.ExecuteScalar(
        "SELECT COUNT(*) FROM dbo.work_order WHERE CHARINDEX(N'[IoT自动报修]', FaultDesc) > 0") ?? 0);

    int frames = 0;
    string? autoOrder = null;
    using var gw = new MqttGateway();
    gw.TelemetryReceived += _ => Interlocked.Increment(ref frames);
    gw.AutoOrderCreated += (no, _) => autoOrder = no;
    gw.StartAsync(withSimulator: false).Wait(15000);
    var dev = devices[0];
    int before = iotCount();

    gw.PublishTelemetry(new TelemetryFrame { DeviceId = dev.DeviceId, DeviceName = dev.DeviceName, Temp = 95.0, Vibration = 9.5 });
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (frames < 1 && sw.Elapsed.TotalSeconds < 10) Thread.Sleep(200);
    Thread.Sleep(1200);   // 等自动报修建单落库
    gw.PublishTelemetry(new TelemetryFrame { DeviceId = dev.DeviceId, DeviceName = dev.DeviceName, Temp = 96.0, Vibration = 9.8 });
    Thread.Sleep(1200);   // 冷却窗内第二帧（10 分钟冷却，不应再建单）
    int after = iotCount();

    Check("MQTT 遥测帧接收", frames >= 1, $"收到 {frames} 帧");
    Check("越限自动报修（确定性输入）", autoOrder != null && after == before + 1, autoOrder ?? "未建单");
    Check("冷却窗内重复告警不重复建单", after == before + 1, $"前 {before} 后 {after}");
    gw.StopAsync().Wait(5000);
}
catch (Exception ex)
{
    Check("MQTT 内嵌Broker遥测接收", false, ex.InnerException?.Message ?? ex.Message);
}

Console.WriteLine($"\n结果：{pass} 通过 / {fail} 失败");
return fail == 0 ? 0 : 1;
