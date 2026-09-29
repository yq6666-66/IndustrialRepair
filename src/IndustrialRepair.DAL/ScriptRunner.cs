using System;
using System.IO;
using System.Text.RegularExpressions;

namespace IndustrialRepair.DAL;

/// <summary>
/// SQL 脚本执行器：按 GO 分批执行 .sql 文件（用于“一键初始化数据库”）。
/// </summary>
public static partial class ScriptRunner
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    /// <summary>执行脚本文件，返回影响的批次数</summary>
    public static int RunFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到脚本文件：{path}");
        return RunText(File.ReadAllText(path));
    }

    public static int RunText(string script)
    {
        int batches = 0;
        var lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var buffer = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (GoLine().IsMatch(line))
            {
                ExecBatch(buffer.ToString());
                buffer.Clear();
                batches++;
            }
            else
            {
                buffer.AppendLine(line);
            }
        }
        if (buffer.Length > 0)
        {
            ExecBatch(buffer.ToString());
            batches++;
        }
        return batches;
    }

    private static void ExecBatch(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        DbHelper.ExecuteNonQuery(sql);
    }
}
