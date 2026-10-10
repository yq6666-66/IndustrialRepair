using System;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Text.RegularExpressions;

namespace IndustrialRepair.DAL;

/// <summary>
/// SQL 脚本执行器：按 GO 分批执行 .sql 文件。
/// 支持两种执行方式：
/// ① 独立连接逐批执行（历史行为，仅用于幂等脚本的手动执行）；
/// ② 共享连接 + 事务执行（迁移运行器使用：所有 GO 批次同一事务，失败整体回滚）。
/// </summary>
public static partial class ScriptRunner
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    /// <summary>执行脚本文件（独立连接，逐批自动提交），返回批次数</summary>
    public static int RunFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到脚本文件：{path}");
        return RunText(File.ReadAllText(path));
    }

    /// <summary>执行脚本文本（独立连接，逐批自动提交）</summary>
    public static int RunText(string script)
    {
        int batches = 0;
        foreach (var batch in SplitBatches(script))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                DbHelper.ExecuteNonQuery(batch);
                batches++;
            }
        }
        return batches;
    }

    /// <summary>执行脚本文本（共享连接与事务：任一批次失败整体回滚），返回批次数</summary>
    public static int RunText(string script, SqlConnection conn, SqlTransaction tran)
    {
        if (conn.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("迁移连接未打开。");
        int batches = 0;
        foreach (var batch in SplitBatches(script))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var cmd = new SqlCommand(batch, conn, tran) { CommandTimeout = 120 };
            cmd.ExecuteNonQuery();
            batches++;
        }
        return batches;
    }

    /// <summary>
    /// 托管执行预处理：删除 USE 语句行（运行器已连到目标库）
    /// 与 SKIP 标记之间的「建库段」（运行器已按目标库名建库）。
    /// </summary>
    public static string PrepareManagedScript(string script)
    {
        var lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var sb = new System.Text.StringBuilder();
        bool skipping = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("/* ===== MIGRATION-RUNNER:SKIP-START", StringComparison.OrdinalIgnoreCase))
            { skipping = true; continue; }
            if (line.StartsWith("/* ===== MIGRATION-RUNNER:SKIP-END", StringComparison.OrdinalIgnoreCase))
            { skipping = false; continue; }
            if (skipping) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^USE\s+\[?[A-Za-z0-9_]+\]?\s*;?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                continue;
            sb.AppendLine(raw);
        }
        return sb.ToString();
    }

    /// <summary>按 GO 行切分脚本为批次（不解析 SQL 注释内的 GO，与 SSMS 行为一致）</summary>
    public static System.Collections.Generic.List<string> SplitBatches(string script)
    {
        var list = new System.Collections.Generic.List<string>();
        var lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var buffer = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (GoLine().IsMatch(line))
            {
                list.Add(buffer.ToString());
                buffer.Clear();
            }
            else
            {
                buffer.AppendLine(line);
            }
        }
        if (buffer.Length > 0) list.Add(buffer.ToString());
        return list;
    }
}
