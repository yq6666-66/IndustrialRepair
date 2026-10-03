// 演示/压测数据生成工具（R1）：批量生成跨月分布的工单（含审阅/领料/出库/完工），
// 用于分页、大屏、报表在真实数据量下的演示与压测。
// 与冒烟同规：必须显式 --conn 指向独立测试库（业务库名拒绝启动）。
// 用法：dotnet run --project test/SeedDemoData -- --conn="<测试库>" --orders=500 [--months=6]
using System.Data;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

var connArg = Array.Find(args, a => a.StartsWith("--conn=", StringComparison.OrdinalIgnoreCase));
var conn = connArg?["--conn=".Length..] ?? Environment.GetEnvironmentVariable("INDUSTRIALREPAIR_TEST_CONN");
if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("× 必须显式指定独立测试库连接串：--conn=\"...\"（或 INDUSTRIALREPAIR_TEST_CONN）。");
    return 2;
}
var dbName = System.Text.RegularExpressions.Regex.Match(conn,
    @"(?:Database|Initial Catalog)\s*=\s*([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.Trim();
if (dbName.Equals("IndustrialRepairDb", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("× 拒绝：业务库 IndustrialRepairDb 不允许生成压测数据。");
    return 2;
}
Db.UseConnectionString(conn);

int orders = int.TryParse(Array.Find(args, a => a.StartsWith("--orders="))?["--orders=".Length..], out var n) && n > 0 ? Math.Min(n, 5000) : 500;
int months = int.TryParse(Array.Find(args, a => a.StartsWith("--months="))?["--months=".Length..], out var m) && m > 0 ? Math.Min(m, 12) : 6;

Console.WriteLine($"▶ 目标库 {dbName}：生成 {orders} 张工单（跨 {months} 个月，含审阅/领料/出库/完工）");
new DbInitializer().EnsureAll();   // 库就绪（幂等；已有结构/账号不重播种）

var devices = new BasicRepository().ListDevices();
var users = new UserRepository();
var reporter = users.GetByUserName("reporter") ?? users.ListByRole("Reporter")[0];
var engineer = users.GetByUserName("engineer") ?? users.ListByRole("Engineer")[0];
var orderRepo = new WorkOrderRepository();
var pickRepo = new PickingRepository();

var rnd = new Random(20261003);   // 固定种子：可复现
// NR9：Bogus 随机化器生成逼真故障描述（固定种子可复现）
var bogus = new Bogus.Randomizer(20261003);
string NextFault() =>
    $"{bogus.PickRandom(new[] { "主轴", "液压站", "输送带", "电柜", "减速机", "冷却水", "气动", "导轨" })}" +
    $"{bogus.PickRandom(new[] { "异响，温度偏高", "压力波动，油缸渗油", "跑偏，托辊磨损", "接触器频繁跳闸", "渗油，密封老化", "温升异常，流量不足", "漏气，压力下降", "润滑不足，阻力增大" })}" +
    $"（{bogus.PickRandom(new[] { "需现场确认", "已通知工艺", "影响产量", "临时处理中", "班中巡检发现" })}）";
string[] types = { "机械", "电气", "液压", "其他" };

int created = 0, reviewed = 0, picked = 0, issued = 0, done = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();

for (int i = 0; i < orders; i++)
{
    // 时间回拨：CreatedAt 由触发器/默认值取当前——为跨月分布，直接 SQL 指定创建时间
    var day = DateTime.Now.AddDays(-rnd.Next(1, months * 30));
    var dev = devices[rnd.Next(devices.Count)];
    var desc = $"{NextFault()}（批量 #{i + 1}）";

    int orderId = int.Parse(orderRepo.Create(dev.DeviceId, reporter.UserId, desc,
        types[rnd.Next(types.Length)], PriorityCode.Normal, null, null));
    created++;

    // 回写创建时间（报表/趋势跨月分布的关键）
    DbHelper.ExecuteNonQuery("UPDATE dbo.work_order SET CreatedAt=@d WHERE OrderId=@id",
        CommandType.Text, DbHelper.P("@d", day), DbHelper.P("@id", orderId));

    int roll = rnd.Next(100);
    if (roll < 15) continue;                        // 15% 停在待派单
    orderRepo.Assign(orderId, engineer.UserId, engineer.UserId);
    if (roll < 30) continue;                        // 15% 停在已派单

    orderRepo.Take(orderId, engineer.UserId);
    orderRepo.ReviewMaterials(orderId, MaterialReviewMode.BomOnly, null, null, "批量数据：整机 BOM", engineer.UserId);
    reviewed++;

    if (roll < 45) continue;                        // 15% 停在维修中（已审阅未领料）
    try
    {
        var pid = pickRepo.Generate(orderId, engineer.UserName, 1.10m);
        picked++;
        if (roll < 60) continue;                    // 15% 留 Pending 领料单
        pickRepo.Issue(pid, engineer.UserName);
        issued++;
        if (roll < 92)                              // ~32% 完工，其余维修中持有已出库单
        {
            orderRepo.Complete(orderId, engineer.UserId, "批量数据：维修完成");
            DbHelper.ExecuteNonQuery("UPDATE dbo.work_order SET FinishTime=@d WHERE OrderId=@id",
                CommandType.Text, DbHelper.P("@d", day.AddHours(rnd.Next(2, 48))), DbHelper.P("@id", orderId));
            done++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   × #{orderId} 领料跳过：{ex.Message}");
    }
}

sw.Stop();
var cnt = DbHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.work_order")?.ToString();
Console.WriteLine($"√ 完成：建单 {created} / 审阅 {reviewed} / 领料 {picked} / 出库 {issued} / 完工 {done}；" +
                  $"库内工单总数 {cnt}，耗时 {sw.Elapsed.TotalSeconds:F1}s");
return 0;
