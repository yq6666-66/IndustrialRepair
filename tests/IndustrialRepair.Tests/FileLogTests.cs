using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>NR5：文件日志 API 契约（Serilog 内核，公共行为不变）。</summary>
public class FileLogTests
{
    private static string ReadShared(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    [Fact]
    public void 写日志后当日滚动文件存在且包含内容()
    {
        var marker = $"unit_{Guid.NewGuid():N}";
        FileLog.Write(marker);

        var file = Path.Combine(FileLog.LogDir, $"app_{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(file), file);
        Assert.Contains(marker, ReadShared(file));   // UI 进程可能并行持有写句柄
    }

    [Fact]
    public void 异常重载写错误级别()
    {
        FileLog.Write(new InvalidOperationException("nr5-test"), "单测上下文");
        var text = ReadShared(Path.Combine(FileLog.LogDir, $"app_{DateTime.Now:yyyyMMdd}.log"));
        Assert.Contains("nr5-test", text);
        Assert.Contains("ERR", text);      // Serilog 三位级别输出
    }
}
