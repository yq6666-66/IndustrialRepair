using System;
using System.Data.SqlClient;
using System.IO;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 数据库一键初始化：建库脚本 → 默认用户（bcrypt）→ 演示工单。
/// 学生自带电脑场景：登录界面点“一键初始化”即可完成部署。
/// </summary>
public class DbInitializer
{
    private readonly BasicRepository _basic = new();
    private readonly UserRepository _users = new();
    private readonly WorkOrderRepository _orders = new();
    private readonly PickingRepository _picking = new();

    private static string FindScript(string file)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "db", file),
            Path.Combine(AppContext.BaseDirectory, file),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "db", file)
        };
        foreach (var p in candidates) if (File.Exists(p)) return p;
        return "";
    }

    /// <summary>执行初始化，返回日志文本（供 UI 显示）</summary>
    public string EnsureAll(Action<string>? progress = null)
    {
        var log = new System.Text.StringBuilder();
        void Say(string s) { log.AppendLine(s); progress?.Invoke(s); }

        try
        {
            if (!_basic.IsDatabaseReady())
            {
                var init = FindScript("01_init.sql");
                var procs = FindScript("02_procs.sql");
                if (init.Length == 0) { Say("× 未找到 db/01_init.sql，请将 db 目录复制到程序目录。"); return log.ToString(); }
                Say($"▶ 执行 {Path.GetFileName(init)} …");
                ScriptRunner.RunFile(init);
                if (procs.Length > 0) { ScriptRunner.RunFile(procs); Say($"▶ 执行 {Path.GetFileName(procs)} …"); }
                Say("√ 数据库结构创建完成。");
            }
            else Say("√ 数据库结构已就绪。");

            if (!_basic.HasUsers())
            {
                SeedUsers();
                Say("√ 默认账号播种完成（密码均为 123456）。");
            }
            else Say("√ 用户账号已存在。");

            if (Convert.ToInt32(DbHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.work_order") ?? 0) == 0)
            {
                SeedDemoOrders();
                Say("√ 演示工单已生成（便于大屏/报表展示）。");
            }
            Say("★ 初始化完成，可以使用系统。");
        }
        catch (SqlException ex)
        {
            Say("× " + DbHelper.FriendlyError(ex));
        }
        catch (Exception ex)
        {
            Say("× " + ex.Message);
        }
        return log.ToString();
    }

    /// <summary>默认四角色账号（对应任务书 RBAC）</summary>
    private void SeedUsers()
    {
        var roles = _users.ListRoles();
        int Role(string code) => roles.Find(r => r.RoleCode == code)!.RoleId;
        var hash = PasswordHasher.Hash("123456");

        void Add(string name, string real, string role, string? email = null, bool mfa = false) =>
            _users.InsertUserWithLog(new UserInfo { UserName = name, RealName = real, RoleId = Role(role), Email = email }, hash, "seed");

        Add("admin", "系统管理员", "Admin", "admin@demo.com");
        Add("warehouse", "库管-王仓", "Warehouse", "wh@demo.com");
        Add("engineer", "工程师-李工", "Engineer", "eng@demo.com", mfa: true);
        Add("reporter", "报修人-张三", "Reporter", "rep@demo.com");
        // 工程师 MFA 开关单独开（上面 mfa 参数仅示意，实际写入如下）
        var eng = _users.GetByUserName("engineer");
        if (eng != null) _users.SetMfa(eng.UserId, false, null);
    }

    /// <summary>生成演示工单：1 已完成（含已出库领料）、1 维修中、1 待派单</summary>
    private void SeedDemoOrders()
    {
        var rep = _users.GetByUserName("reporter")!;
        var eng = _users.GetByUserName("engineer")!;
        var devices = _basic.ListDevices();

        int Create(int devIndex, string desc, string type, string prio, string recognized) =>
            int.Parse(_orders.Create(devices[devIndex].DeviceId, rep.UserId, desc, type, prio, null, recognized));

        var o1 = Create(0, "主轴运转异响，温度偏高", "机械", "Urgent", "疑似：深沟球轴承6205/骨架油封");
        _orders.Assign(o1, eng.UserId, eng.UserId);
        _orders.Take(o1, eng.UserId);
        var pid = _picking.Generate(o1, eng.UserName, 1.10m);
        _picking.Issue(pid, eng.UserName);
        _orders.Complete(o1, eng.UserId, "更换轴承与油封，运转恢复正常。");

        var o2 = Create(4, "液压站压力波动，油缸渗油", "液压", "Critical", "疑似：活塞密封修理包");
        _orders.Assign(o2, eng.UserId, eng.UserId);
        _orders.Take(o2, eng.UserId);

        Create(5, "输送带跑偏，托辊异响", "机械", "Normal", "");
    }
}
