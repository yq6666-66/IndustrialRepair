using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace IndustrialRepair.DAL;

/// <summary>
/// ADO.NET 访问助手：SqlConnection / SqlCommand / SqlDataAdapter / DataSet、
/// 显式事务 BeginTransaction-Commit-Rollback、SqlException 友好化。
/// 所有 SQL 均通过 SqlParameter 参数化执行，杜绝拼接注入。
/// </summary>
public static class DbHelper
{
    private static SqlConnection Open()
    {
        // LocalDB 冷启动可能较慢/瞬时失败：最多重试 3 次（工程化优化）
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var conn = new SqlConnection(Db.ConnectionString);
                conn.Open();
                return conn;
            }
            catch (SqlException ex) when (attempt < 3 && (ex.Number == -2 || ex.Number == 4060 || ex.Number == 233 || ex.Number == 10061))
            {
                System.Threading.Thread.Sleep(700 * attempt);   // LocalDB 实例自动拉起等待
            }
        }
    }

    /// <summary>增删改（含存储过程）</summary>
    public static int ExecuteNonQuery(string sql, CommandType type = CommandType.Text, params SqlParameter[] ps)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(sql, conn) { CommandType = type };
        if (ps.Length > 0) cmd.Parameters.AddRange(ps);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>查询单值</summary>
    public static object? ExecuteScalar(string sql, CommandType type = CommandType.Text, params SqlParameter[] ps)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(sql, conn) { CommandType = type };
        if (ps.Length > 0) cmd.Parameters.AddRange(ps);
        return cmd.ExecuteScalar();
    }

    /// <summary>DataAdapter 填充 DataTable</summary>
    public static DataTable GetTable(string sql, CommandType type = CommandType.Text, params SqlParameter[] ps)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(sql, conn) { CommandType = type };
        if (ps.Length > 0) cmd.Parameters.AddRange(ps);
        using var adapter = new SqlDataAdapter(cmd);
        var table = new DataTable();
        adapter.Fill(table);
        return table;
    }

    /// <summary>DataAdapter 填充 DataSet（多结果集）</summary>
    public static DataSet GetDataSet(string sql, CommandType type = CommandType.Text, params SqlParameter[] ps)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(sql, conn) { CommandType = type };
        if (ps.Length > 0) cmd.Parameters.AddRange(ps);
        using var adapter = new SqlDataAdapter(cmd);
        var ds = new DataSet();
        adapter.Fill(ds);
        return ds;
    }

    /// <summary>
    /// 执行存储过程并取回输出参数（如 usp_CreateOrder 的 @NewOrderId）。
    /// 返回受影响行数；输出参数值通过 outParams 字典取回。
    /// </summary>
    public static int ExecProc(string procName, Dictionary<string, object?>? inputs,
                               Dictionary<string, SqlDbType>? outputs, out Dictionary<string, object?> outValues)
    {
        using var conn = Open();
        using var cmd = new SqlCommand(procName, conn) { CommandType = CommandType.StoredProcedure };
        if (inputs != null)
            foreach (var kv in inputs)
                cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
        if (outputs != null)
            foreach (var kv in outputs)
                cmd.Parameters.Add(new SqlParameter(kv.Key, kv.Value) { Direction = ParameterDirection.Output });
        int rows = cmd.ExecuteNonQuery();
        outValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (outputs != null)
            foreach (var kv in outputs)
                outValues[kv.Key] = cmd.Parameters[kv.Key].Value == DBNull.Value ? null : cmd.Parameters[kv.Key].Value;
        return rows;
    }

    /// <summary>
    /// C# 侧显式事务演示：BeginTransaction → 多条命令 → Commit / 异常 Rollback。
    /// body 内使用同一连接与事务执行命令。
    /// </summary>
    public static void RunInTransaction(Action<SqlConnection, SqlTransaction> body)
    {
        using var conn = Open();
        using var tran = conn.BeginTransaction();
        try
        {
            body(conn, tran);
            tran.Commit();
        }
        catch
        {
            tran.Rollback();
            throw;
        }
    }

    /// <summary>SqlException → 中文友好提示（登录/外键/唯一键等）</summary>
    public static string FriendlyError(Exception ex)
    {
        return ex switch
        {
            SqlException se => se.Number switch
            {
                2601 or 2627 => "数据已存在（编号/名称重复），请更换后重试。",
                547 => "该记录已被业务数据引用，无法删除。",
                1205 => "数据库忙（死锁），请稍后重试。",
                -2 => "操作超时，请稍后重试。",
                4060 => "数据库不存在，请先执行初始化脚本。",
                _ => se.Message
            },
            InvalidOperationException => "数据库连接失败，请检查 SQL Server LocalDB 服务是否启动。",
            _ => ex.Message
        };
    }

    public static SqlParameter P(string name, object value) =>
        new(name, value ?? DBNull.Value);
}
