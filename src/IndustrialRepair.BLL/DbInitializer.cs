using System;
using System.Data.SqlClient;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
namespace IndustrialRepair.BLL;

/// <summary>
/// 数据库结构管理器（M0 升级机制）：
/// 1) 版本化迁移：dbo.schema_version 记录已应用版本；每次迁移的所有 GO 批次
///    在同一连接同一事务内执行，任一批次失败整体回滚（版本号不落库，可重试）；
/// 2) 过程刷新：02_procs.sql 按文件 SHA-256 与库级扩展属性比对，变化才重建（CREATE OR ALTER）；
/// 3) 结构校验：目标版本的关键列/索引/对象全部存在才算成功，否则回滚；
/// 4) 空库：先连 master 按目标库名建库，再连目标库执行 01 基线 + 04 迁移 + 02 过程；
///    旧库：补登记基线版本后应用待迁移版本，不重新播种演示数据。
/// 播种（默认账号/演示工单）仅在 EnsureAll（一键初始化）路径执行，正常启动只做结构检查。
/// </summary>
public class DbInitializer
{
    /// <summary>当前代码要求的结构版本（与 db/04+05+06 迁移脚本对应）</summary>
    public const int SchemaVersion = 4;

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

    /// <summary>结构就绪（含版本与关键列校验），供启动检查使用</summary>
    public bool IsStructureReady()
    {
        try
        {
            var v = DbHelper.ExecuteScalar(
                @"SELECT CASE WHEN OBJECT_ID('dbo.schema_version') IS NULL THEN -1
                              ELSE ISNULL((SELECT MAX(Version) FROM dbo.schema_version), 0) END");
            return Convert.ToInt32(v ?? -1) >= SchemaVersion;
        }
        catch { return false; }
    }

    /// <summary>
    /// 结构检查与升级（登录前调用；不播种任何数据）。
    /// 失败抛异常并写日志——由调用方阻止进入业务页面，重启可重试。
    /// </summary>
    public void EnsureStructure()
    {
        var target = new SqlConnectionStringBuilder(Db.ConnectionString);
        var dbName = target.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName) || !System.Text.RegularExpressions.Regex.IsMatch(dbName, @"^[A-Za-z0-9_]+$"))
            throw new InvalidOperationException($"连接串未指定合法的数据库名：{dbName ?? "(空)"}");

        // 1) 目标库不存在则先建（连 master，库级操作在事务外）
        using (var master = new SqlConnection(new SqlConnectionStringBuilder(Db.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            OpenWithRetry(master);
            using var cmd = new SqlCommand(
                "IF DB_ID(@name) IS NULL CREATE DATABASE [" + dbName + "]", master);
            cmd.Parameters.AddWithValue("@name", dbName);
            cmd.ExecuteNonQuery();
        }

        // 2) 目标库内：建版本表 → 应用待迁移版本 → 视哈希刷新过程 → 结构校验；同一事务
        using var conn = new SqlConnection(Db.ConnectionString);
        OpenWithRetry(conn);
        // 含过滤索引/计算列表对象的 DML 与 CREATE OR ALTER 均要求本会话选项（与 02 文件头双保险）
        using (var qi = new SqlCommand("SET QUOTED_IDENTIFIER ON", conn)) qi.ExecuteNonQuery();
        using var tran = conn.BeginTransaction();
        try
        {
            Exec(conn, tran, @"IF OBJECT_ID(N'dbo.schema_version', N'U') IS NULL
CREATE TABLE dbo.schema_version(
    Version     INT           NOT NULL PRIMARY KEY,
    AppliedAt   DATETIME      NOT NULL DEFAULT(GETDATE()),
    Description NVARCHAR(200) NOT NULL
);");

            int current = Convert.ToInt32(ExecScalar(conn, tran,
                "SELECT ISNULL((SELECT MAX(Version) FROM dbo.schema_version), 0)"));

            if (current < 1)
            {
                RunManaged(conn, tran, "01_init.sql",
                    "基线：建表 + 基础种子（角色/权限/字典/配件/设备/BOM）");
                InsertVersion(conn, tran, 1, "基线结构（01_init.sql）");
                current = 1;
            }

            if (current < 2)
            {
                RunManaged(conn, tran, "04_material_review_migration.sql",
                    "材料审阅字段 + 写入校验（CHECK 约束 / 一工单一张 Pending 单唯一索引）");
                InsertVersion(conn, tran, 2, "材料审阅与写入校验（04_material_review_migration.sql）");
                current = 2;
            }

            if (current < 3)
            {
                RunManaged(conn, tran, "05_performance_indexes.sql",
                    "性能索引：工单 SLA 扫描/配件搜索/库存流水/审计查询 6 条复合索引");
                InsertVersion(conn, tran, 3, "性能索引（05_performance_indexes.sql）");
                current = 3;
            }

            if (current < 4)
            {
                RunManaged(conn, tran, "06_new_features.sql",
                    "新功能：PM调度/供应商/停机追踪/配件-供应商关联");
                InsertVersion(conn, tran, 4, "新功能表（06_new_features.sql）");
                current = 4;
            }

            if (current > SchemaVersion)
                throw new InvalidOperationException(
                    $"数据库结构版本({current})高于程序支持版本({SchemaVersion})，请升级程序。");

            // 3) 过程/触发器/视图：按文件哈希增量刷新
            var procsPath = FindScript("02_procs.sql");
            if (procsPath.Length == 0)
                throw new FileNotFoundException("未找到 db/02_procs.sql（发布包需包含 db 目录）。");
            var procsText = File.ReadAllText(procsPath);
            var hash = Sha256(procsText);
            var stored = ExecScalar(conn, tran,
                "SELECT CONVERT(NVARCHAR(100), value) FROM fn_listextendedproperty(N'procs_sha256', NULL, NULL, NULL, NULL, NULL, NULL)") as string;
            if (!string.Equals(stored, hash, StringComparison.OrdinalIgnoreCase))
            {
                ScriptRunner.RunText(ScriptRunner.PrepareManagedScript(procsText), conn, tran);
                Exec(conn, tran, stored == null
                    ? "EXEC sp_addextendedproperty @name=N'procs_sha256', @value=@hash"
                    : "EXEC sp_updateextendedproperty @name=N'procs_sha256', @value=@hash",
                    new SqlParameter("@hash", hash));
            }

            // 4) 结构校验：目标版本关键对象必须齐备，否则整体回滚
            int missing = Convert.ToInt32(ExecScalar(conn, tran, VerifySql));
            if (missing != 0)
                throw new InvalidOperationException($"结构校验未通过：缺少 {missing} 个关键对象/列，已回滚。");

            tran.Commit();
        }
        catch
        {
            try { tran.Rollback(); } catch { }
            throw;
        }
    }

    /// <summary>执行初始化并播种（登录窗「一键初始化」按钮）：结构 + 默认账号 + 演示工单</summary>
    public string EnsureAll(Action<string>? progress = null)
    {
        var log = new StringBuilder();
        void Say(string s) { log.AppendLine(s); progress?.Invoke(s); }
        try
        {
            EnsureStructure();
            Say("√ 数据库结构就绪（含迁移与存储过程）。");

            if (Convert.ToInt32(DbHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.sys_user") ?? 0) == 0)
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
        catch (SqlException ex) { Say("× " + DbHelper.FriendlyError(ex)); }
        catch (Exception ex) { Say("× " + ex.Message); }
        return log.ToString();
    }

    /*==================== 私有辅助 ================*/

    private const string VerifySql = @"
SELECT
 (CASE WHEN OBJECT_ID(N'dbo.schema_version') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'RecognizedJson') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'MaterialReviewMode') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'ConfirmedPartId') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'ConfirmedBaseQty') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'ReviewNote') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'ReviewedBy') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.work_order', N'ReviewedAt') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN COL_LENGTH(N'dbo.picking_list', N'ReviewSnapshot') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UQ_picking_pending_per_order') THEN 1 ELSE 0 END)+
 (CASE WHEN OBJECT_ID(N'dbo.usp_GeneratePicking', N'P') IS NULL THEN 1 ELSE 0 END)+
 (CASE WHEN OBJECT_ID(N'dbo.trg_WorkOrder_Record', N'TR') IS NULL THEN 1 ELSE 0 END)";

    private static void RunManaged(SqlConnection conn, SqlTransaction tran, string file, string desc)
    {
        var path = FindScript(file);
        if (path.Length == 0)
            throw new FileNotFoundException($"未找到 db/{file}（发布包需包含 db 目录）。");
        FileLog.Write($"应用数据库迁移：{file}（{desc}）");
        ScriptRunner.RunText(ScriptRunner.PrepareManagedScript(File.ReadAllText(path)), conn, tran);
    }

    private static void InsertVersion(SqlConnection conn, SqlTransaction tran, int version, string desc) =>
        Exec(conn, tran,
            "IF NOT EXISTS(SELECT 1 FROM dbo.schema_version WHERE Version=@v) " +
            "INSERT INTO dbo.schema_version(Version, Description) VALUES(@v, @d)",
            new SqlParameter("@v", version), new SqlParameter("@d", desc));

    private static void Exec(SqlConnection conn, SqlTransaction tran, string sql, params SqlParameter[] ps)
    {
        using var cmd = new SqlCommand(sql, conn, tran) { CommandTimeout = 180 };
        if (ps.Length > 0) cmd.Parameters.AddRange(ps);
        cmd.ExecuteNonQuery();
    }

    private static object? ExecScalar(SqlConnection conn, SqlTransaction tran, string sql)
    {
        using var cmd = new SqlCommand(sql, conn, tran) { CommandTimeout = 180 };
        return cmd.ExecuteScalar();
    }

    private static void OpenWithRetry(SqlConnection conn)
    {
        // 与 DbHelper.Open 共用 Polly 管道（NR5，消除重复实现）
        SqlResilience.Retry.Execute(() =>
        {
            if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        });
    }

    private static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }

    /*==================== 播种（仅一键初始化路径）====================*/

    /// <summary>默认四角色账号（对应任务书 RBAC）</summary>
    private void SeedUsers()
    {
        var roles = _users.ListRoles();
        int Role(string code) => roles.Find(r => r.RoleCode == code)!.RoleId;
        var hash = PasswordHasher.Hash("123456");

        void Add(string name, string real, string role, string? email = null) =>
            _users.InsertUserWithLog(new UserInfo { UserName = name, RealName = real, RoleId = Role(role), Email = email }, hash, "seed");

        Add("admin", "系统管理员", "Admin", "admin@demo.com");
        Add("warehouse", "库管-王仓", "Warehouse", "wh@demo.com");
        Add("engineer", "工程师-李工", "Engineer", "eng@demo.com");
        Add("reporter", "报修人-张三", "Reporter", "rep@demo.com");
    }

    /// <summary>生成演示工单：1 已完成（含已出库领料）、1 维修中、1 待派单</summary>
    private void SeedDemoOrders()
    {
        var rep = _users.GetByUserName("reporter")!;
        var eng = _users.GetByUserName("engineer")!;
        var devices = new DAL.BasicRepository().ListDevices();

        int Create(int devIndex, string desc, string type, string prio, string recognized) =>
            int.Parse(_orders.Create(devices[devIndex].DeviceId, rep.UserId, desc, type, prio, null, recognized));

        var o1 = Create(0, "主轴运转异响，温度偏高", "机械", "Urgent", "疑似：深沟球轴承6205/骨架油封");
        _orders.Assign(o1, eng.UserId, eng.UserId);
        _orders.Take(o1, eng.UserId);
        // M1：生成领料前必须完成材料审阅（演示播种直接走仓储/过程，不经服务层会话守卫）
        _orders.ReviewMaterials(o1, MaterialReviewMode.BomOnly, null, null, "演示：按整机 BOM 配料", eng.UserId);
        var pid = _picking.Generate(o1, eng.UserName, 1.10m);
        _picking.Issue(pid, eng.UserName);
        _orders.Complete(o1, eng.UserId, "更换轴承与油封，运转恢复正常。");

        var o2 = Create(4, "液压站压力波动，油缸渗油", "液压", "Critical", "疑似：活塞密封修理包");
        _orders.Assign(o2, eng.UserId, eng.UserId);
        _orders.Take(o2, eng.UserId);

        Create(5, "输送带跑偏，托辊异响", "机械", "Normal", "");
    }
}
