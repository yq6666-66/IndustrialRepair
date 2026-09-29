using System;
using System.Data.SqlClient;
using System.IO;
using System.Text.Json;

namespace IndustrialRepair.DAL;

/// <summary>
/// 数据库连接配置。优先读取运行目录 appsettings.json 的 ConnectionString 节点，
/// 找不到则使用 LocalDB 默认值。
/// </summary>
public static class Db
{
    private const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=IndustrialRepairDb;Trusted_Connection=True;TrustServerCertificate=True";

    public static string ConnectionString { get; private set; } = DefaultConnectionString;

    static Db() => LoadConfig();

    private static void LoadConfig()
    {
        try
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                Path.Combine(AppContext.BaseDirectory, "appsettings.db.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json")
            };
            foreach (var file in candidates)
            {
                if (!File.Exists(file)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("ConnectionString", out var node))
                {
                    var s = node.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) { ConnectionString = s; return; }
                }
            }
        }
        catch
        {
            // 配置读取失败时保持默认连接串
        }
    }

    /// <summary>测试数据库连通性</summary>
    public static bool TryConnect(out string error)
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
