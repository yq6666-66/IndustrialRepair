using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 数据库备份与恢复（OR2）：BACKUP DATABASE / RESTORE DATABASE + 备份文件列表管理。
/// 仅 sysadmin（user.manage 权限）可操作；恢复前要求确认（会覆盖当前数据）。
/// 备份目录：%LocalAppData%\IndustrialRepair\backups
/// </summary>
public class BackupService
{
    public static string BackupDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndustrialRepair", "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public sealed record BackupInfo(string FileName, string FullPath, long SizeBytes, DateTime CreatedAt);

    /// <summary>执行备份（BACKUP DATABASE WITH INIT 覆盖同名文件）。</summary>
    public Result<string> Backup(string tag = "")
    {
        try
        {
            var safeTag = string.IsNullOrWhiteSpace(tag) ? "" : "_" + string.Join("_",
                tag.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var file = Path.Combine(BackupDir,
                $"IndustrialRepairDb_{DateTime.Now:yyyyMMdd_HHmmss}{safeTag}.bak");
            DbHelper.ExecuteNonQuery(
                $"BACKUP DATABASE [{Db.DatabaseName}] TO DISK='{file.Replace("'", "''")}' WITH INIT",
                CommandType.Text);
            return Result<string>.Ok(file, $"备份完成：{Path.GetFileName(file)}（{new FileInfo(file).Length / 1024} KB）");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(DbHelper.FriendlyError(ex));
        }
    }

    /// <summary>列出备份目录中全部 .bak 文件（按时间降序）。</summary>
    public List<BackupInfo> ListBackups()
    {
        var list = new List<BackupInfo>();
        foreach (var file in Directory.GetFiles(BackupDir, "*.bak"))
        {
            var fi = new FileInfo(file);
            list.Add(new BackupInfo(fi.Name, file, fi.Length, fi.CreationTime));
        }
        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>
    /// 从备份恢复（RESTORE DATABASE WITH REPLACE）。
    /// ⚠ 会覆盖当前全部数据——调用方须二次确认。
    /// </summary>
    public Result Restore(BackupInfo backup)
    {
        try
        {
            if (!File.Exists(backup.FullPath))
                return Result.Fail("备份文件不存在。");

            var path = backup.FullPath.Replace("'", "''");
            var db = Db.DatabaseName;
            DbHelper.ExecuteNonQuery(
                $"USE master; ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"RESTORE DATABASE [{db}] FROM DISK='{path}' WITH REPLACE; " +
                $"ALTER DATABASE [{db}] SET MULTI_USER",
                CommandType.Text);
            return Result.Ok($"恢复完成：{backup.FileName}");
        }
        catch (Exception ex)
        {
            // 恢复失败时尝试恢复多用户模式
            try { DbHelper.ExecuteNonQuery($"USE master; ALTER DATABASE [{Db.DatabaseName}] SET MULTI_USER", CommandType.Text); } catch { }
            return Result.Fail(DbHelper.FriendlyError(ex));
        }
    }

    /// <summary>删除备份文件。</summary>
    public Result Delete(BackupInfo backup)
    {
        try
        {
            File.Delete(backup.FullPath);
            return Result.Ok($"已删除 {backup.FileName}");
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}
