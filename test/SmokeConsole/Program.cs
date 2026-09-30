// 无界面端到端冒烟测试：初始化 → 登录 → 报修 → 派单 → 接单 → 算料 → 出库 → 完工 → 大屏/报表/串口解析。
// 运行：dotnet run --project test/SmokeConsole
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {name}{(ok || detail == "" ? "" : " —— " + detail)}");
    if (ok) pass++; else fail++;
}

Console.WriteLine("== 1. 数据库一键初始化（建库+默认用户+演示工单） ==");
var initLog = new DbInitializer().EnsureAll();
Console.WriteLine(initLog);
Check("数据库就绪", new BasicRepository().IsDatabaseReady());

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
var reg = auth.Register("stu_test_" + DateTime.Now.Ticks % 10000, "abc123", "测试学生", "13800000000", "stu@test.com");
Check("注册新账号", reg.Success, reg.Message);

Console.WriteLine("== 3. 工单全流程（报修→派单→接单） ==");
var orderSvc = new WorkOrderService();
var devices = orderSvc.Devices();
Check("设备台账加载", devices.Count > 0);
var reporter = rep.Data!;
var report = orderSvc.Report(devices[6].DeviceId, reporter.UserId, "冒烟：输送带异响", "机械", PriorityCode.Normal, null);
Check("创建报修工单", report.Success, report.Message);
int orderId = int.Parse(report.Data!);

var engineers = new UserRepository().ListByRole("Engineer");
Check("工程师账号存在", engineers.Count > 0);
var engineer = engineers[0];
Check("派单", orderSvc.Assign(orderId, engineer.UserId, reporter.UserId).Success);
Check("接单", orderSvc.Take(orderId, engineer.UserId).Success);
Check("工单综合查询(工单中心列表SQL)", orderSvc.Query(null, null, null, false).Rows.Count > 0);
Check("工单状态过滤(Working)", orderSvc.Query("Working", null, null, false).Rows.Count >= 1);

Console.WriteLine("== 4. 智能算料与领料出库 ==");
var pick = new PickingService();
var gen = pick.Generate(orderId, engineer.UserName, 1.10m);
Check("BOM×1.10 生成领料单", gen.Success, gen.Message);
if (gen.Success)
{
    Check("未出库时重复算料被拦截", !pick.Generate(orderId, engineer.UserName, 1.10m).Success, "应阻止重复生成");
    var items = pick.Items(gen.Data);
    Check("领料明细非空", items.Count > 0, $"共 {items.Count} 项");
    Console.WriteLine("   明细：" + string.Join("；", items.Select(i => $"{i.PartName}×{i.Qty}={i.Amount:F2}")));
    var issue = pick.Issue(gen.Data, engineer.UserName);
    Check("领料出库(事务扣库存)", issue.Success, issue.Message);
    Check("领料单列表查询(领料页列表SQL)", pick.List(null, null).Rows.Count >= 1);
}

Console.WriteLine("== 5. 完工与报表/大屏 ==");
var done = orderSvc.Complete(orderId, engineer.UserId, "冒烟测试完工：更换托辊轴承");
Check("完工登记", done.Success, done.Message);
var records = orderSvc.Records(orderId);
Check("流转记录完整(创建→派单→接单→完工)", records.Count >= 4, string.Join("→", records.Select(r => WorkOrderRecord.ToChinese(r.Action))));
var dash = new DashboardService();
var (today, pending, working, doneCnt, cost) = dash.Summary();
Console.WriteLine($"   大屏汇总：今日{today} 待派{pending} 维修中{working} 已完成{doneCnt} 本月成本{cost}");
Check("大屏数据可用", dash.Rows().Count > 0);
Check("维修报表数据可用", orderSvc.ReportSheet().Rows.Count > 0);

Console.WriteLine("== 6. 库存预警与流水 ==");
var partSvc = new PartService();
var logs = partSvc.StockLogs(0);
Check("库存流水已记录", logs.Rows.Count > 0);

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

// 9.2 OpenCV 识别引擎 V2（生成测试图：灰色圆形零件照片）
var testImg = Path.Combine(Path.GetTempPath(), "smoke_part.png");
using (var bmp = new System.Drawing.Bitmap(300, 300))
{
    using var g = System.Drawing.Graphics.FromImage(bmp);
    g.Clear(System.Drawing.Color.FromArgb(235, 235, 238));
    using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(125, 125, 130));
    g.FillEllipse(brush, 60, 60, 180, 180);
    bmp.Save(testImg, System.Drawing.Imaging.ImageFormat.Png);
}
var recResults = new RecognitionEngine().Recognize(testImg);
Check("识别引擎V2 返回候选", recResults.Count > 0 && recResults[0].Score > 0,
      $"Top1 {recResults[0].PartName} {recResults[0].Score}%：{recResults[0].Reason}");
Check("OpenCV 原生库可用（圆度/轮廓路径）", recResults.Any(r => r.Reason.Contains("OpenCV")),
      "若降级为[规则]路径请检查 OpenCvSharp4.runtime.win");

// 9.3 ClosedXML 导出
var xlsTable = new System.Data.DataTable();
xlsTable.Columns.Add("编码", typeof(string));
xlsTable.Columns.Add("金额", typeof(decimal));
xlsTable.Rows.Add("P-ZC-001", 55.50m);
xlsTable.Rows.Add("P-RH-001", 84.00m);
var xlsFile = ExcelService.ExportTable(xlsTable, "冒烟导出", "Sheet1", totalRow: true);
Check("ClosedXML 导出 .xlsx", File.Exists(xlsFile) && new FileInfo(xlsFile).Length > 4000, xlsFile);

// 9.4 MQTTnet 内嵌 Broker 收发（含遥测上抛）
try
{
    var frames = 0;
    string? autoOrder = null;
    var gw = new MqttGateway();
    gw.TelemetryReceived += _ => Interlocked.Increment(ref frames);
    gw.AutoOrderCreated += (no, _) => autoOrder = no;
    gw.StartAsync().Wait(15000);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (frames < 3 && sw.Elapsed.TotalSeconds < 20) Thread.Sleep(500);
    Check("MQTT 内嵌Broker遥测接收", frames >= 3, $"收到 {frames} 帧");
    gw.StopAsync().Wait(5000);
    Console.WriteLine($"   MQTT 自动报修联动：{(autoOrder == null ? "本轮模拟未触发越限（正常，10%概率冲高）" : "已创建 " + autoOrder)}");
}
catch (Exception ex)
{
    Check("MQTT 内嵌Broker遥测接收", false, ex.InnerException?.Message ?? ex.Message);
}

Console.WriteLine($"\n结果：{pass} 通过 / {fail} 失败");
return fail == 0 ? 0 : 1;
