using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>用户 / 角色 / 权限 / 会话 / 日志</summary>
public class UserRepository
{
    /*-------------- 查询 --------------*/

    public UserInfo? GetByUserName(string userName)
    {
        var t = DbHelper.GetTable(
            @"SELECT u.UserId,u.UserName,u.PasswordHash,u.RealName,u.Phone,u.Email,u.MfaEnabled,u.MfaCode,
                     u.Status,u.FailCount,u.LockedUntil,u.LastLoginTime,u.RoleId,
                     r.RoleCode,r.RoleName
              FROM dbo.sys_user u JOIN dbo.sys_role r ON r.RoleId=u.RoleId
              WHERE u.UserName=@UserName",
            CommandType.Text, DbHelper.P("@UserName", userName));
        if (t.Rows.Count == 0) return null;
        var row = t.Rows[0];
        var hash = row["PasswordHash"].ToString();
        return new UserInfo
        {
            UserId = (int)row["UserId"],
            UserName = row["UserName"].ToString()!,
            RealName = row["RealName"].ToString()!,
            Phone = row["Phone"] == DBNull.Value ? null : row["Phone"].ToString(),
            Email = row["Email"] == DBNull.Value ? null : row["Email"].ToString(),
            MfaEnabled = (bool)row["MfaEnabled"],
            MfaCode = row["MfaCode"] == DBNull.Value ? null : row["MfaCode"].ToString(),
            Status = (bool)row["Status"],
            FailCount = (int)row["FailCount"],
            LockedUntil = row["LockedUntil"] == DBNull.Value ? null : (DateTime?)row["LockedUntil"],
            LastLoginTime = row["LastLoginTime"] == DBNull.Value ? null : (DateTime?)row["LastLoginTime"],
            RoleId = (int)row["RoleId"],
            RoleCode = row["RoleCode"].ToString()!,
            RoleName = row["RoleName"].ToString()!,
            Permissions = GetPermissions((int)row["UserId"])
        };
    }

    /// <summary>口令哈希单独取（登录校验用）</summary>
    public string? GetPasswordHash(int userId) =>
        DbHelper.ExecuteScalar("SELECT PasswordHash FROM dbo.sys_user WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@Id", userId)) as string;

    public HashSet<string> GetPermissions(int userId) =>
        GetPermissionsByRole(GetRoleId(userId));

    public HashSet<string> GetPermissionsByRole(int roleId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var t = DbHelper.GetTable(
            "SELECT PermCode FROM dbo.sys_role_permission WHERE RoleId=@RoleId",
            CommandType.Text, DbHelper.P("@RoleId", roleId));
        foreach (DataRow r in t.Rows) set.Add(r["PermCode"].ToString()!);
        return set;
    }

    public int GetRoleId(int userId) =>
        Convert.ToInt32(DbHelper.ExecuteScalar("SELECT RoleId FROM dbo.sys_user WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@Id", userId)) ?? 0);

    public List<RoleInfo> ListRoles()
    {
        var list = new List<RoleInfo>();
        var t = DbHelper.GetTable("SELECT RoleId,RoleCode,RoleName,Description FROM dbo.sys_role ORDER BY RoleId");
        foreach (DataRow r in t.Rows)
            list.Add(new RoleInfo
            {
                RoleId = (int)r["RoleId"],
                RoleCode = r["RoleCode"].ToString()!,
                RoleName = r["RoleName"].ToString()!,
                Description = r["Description"] == DBNull.Value ? null : r["Description"].ToString()
            });
        return list;
    }

    public DataTable ListUsers(string? keyword)
    {
        return DbHelper.GetTable(
            @"SELECT u.UserId,u.UserName,u.RealName,u.Phone,u.Email,u.Status,u.MfaEnabled,
                     u.LastLoginTime,r.RoleName
              FROM dbo.sys_user u JOIN dbo.sys_role r ON r.RoleId=u.RoleId
              WHERE (@KW IS NULL OR u.UserName LIKE @KWL OR u.RealName LIKE @KWL)
              ORDER BY u.UserId",
            CommandType.Text,
            DbHelper.P("@KW", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword),
            DbHelper.P("@KWL", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : $"%{keyword}%"));
    }

    public List<UserInfo> ListByRole(string roleCode)
    {
        var list = new List<UserInfo>();
        var t = DbHelper.GetTable(
            @"SELECT u.UserId,u.UserName,u.RealName,r.RoleCode,r.RoleName
              FROM dbo.sys_user u JOIN dbo.sys_role r ON r.RoleId=u.RoleId
              WHERE r.RoleCode=@RC AND u.Status=1 ORDER BY u.UserId",
            CommandType.Text, DbHelper.P("@RC", roleCode));
        foreach (DataRow r in t.Rows)
            list.Add(new UserInfo
            {
                UserId = (int)r["UserId"], UserName = r["UserName"].ToString()!,
                RealName = r["RealName"].ToString()!, RoleCode = r["RoleCode"].ToString()!,
                RoleName = r["RoleName"].ToString()!
            });
        return list;
    }

    /*-------------- 写入 --------------*/

    /// <summary>注册新用户 + 写操作日志（C# 侧显式事务演示）</summary>
    public int InsertUserWithLog(UserInfo user, string passwordHash, string regIp)
    {
        object? newId = null;
        DbHelper.RunInTransaction((conn, tran) =>
        {
            using (var cmd = new SqlCommand(
                @"INSERT INTO dbo.sys_user(UserName,PasswordHash,RealName,Phone,Email,RoleId)
                  VALUES(@UserName,@Hash,@RealName,@Phone,@Email,@RoleId);
                  SELECT SCOPE_IDENTITY();", conn, tran))
            {
                cmd.Parameters.AddWithValue("@UserName", user.UserName);
                cmd.Parameters.AddWithValue("@Hash", passwordHash);
                cmd.Parameters.AddWithValue("@RealName", user.RealName);
                cmd.Parameters.AddWithValue("@Phone", (object?)user.Phone ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Email", (object?)user.Email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
                newId = cmd.ExecuteScalar();
            }
            using (var log = new SqlCommand(
                @"INSERT INTO dbo.sys_operation_log(UserId,UserName,Module,Action,Detail)
                  VALUES(@Id,@Name,N'系统',N'注册',N'新用户注册成功')", conn, tran))
            {
                log.Parameters.AddWithValue("@Id", newId ?? 0);
                log.Parameters.AddWithValue("@Name", user.UserName);
                log.ExecuteNonQuery();
            }
        });
        return Convert.ToInt32(newId ?? 0);
    }

    public void UpdatePassword(int userId, string newHash) =>
        DbHelper.ExecuteNonQuery("UPDATE dbo.sys_user SET PasswordHash=@H,FailCount=0,LockedUntil=NULL WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@H", newHash), DbHelper.P("@Id", userId));

    public void SetMfa(int userId, bool enabled, string? code) =>
        DbHelper.ExecuteNonQuery("UPDATE dbo.sys_user SET MfaEnabled=@E,MfaCode=@C WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@E", enabled), DbHelper.P("@C", (object?)code ?? DBNull.Value), DbHelper.P("@Id", userId));

    /// <summary>登录结果落库：失败计数/锁定/最后登录时间</summary>
    public void UpdateLoginState(int userId, bool success)
    {
        if (success)
            DbHelper.ExecuteNonQuery(
                "UPDATE dbo.sys_user SET FailCount=0,LockedUntil=NULL,LastLoginTime=GETDATE() WHERE UserId=@Id",
                CommandType.Text, DbHelper.P("@Id", userId));
        else
            DbHelper.ExecuteNonQuery(
                @"UPDATE dbo.sys_user
                  SET FailCount = FailCount+1,
                      LockedUntil = CASE WHEN FailCount+1>=5 THEN DATEADD(MINUTE,10,GETDATE()) ELSE LockedUntil END
                  WHERE UserId=@Id",
                CommandType.Text, DbHelper.P("@Id", userId));
    }

    public void SetStatus(int userId, bool enabled) =>
        DbHelper.ExecuteNonQuery("UPDATE dbo.sys_user SET Status=@S WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@S", enabled), DbHelper.P("@Id", userId));

    public void SetRole(int userId, int roleId) =>
        DbHelper.ExecuteNonQuery("UPDATE dbo.sys_user SET RoleId=@R WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@R", roleId), DbHelper.P("@Id", userId));

    public void UpdateProfile(int userId, string realName, string? phone, string? email) =>
        DbHelper.ExecuteNonQuery(
            "UPDATE dbo.sys_user SET RealName=@N,Phone=@P,Email=@E WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@N", realName), DbHelper.P("@P", (object?)phone ?? DBNull.Value),
            DbHelper.P("@E", (object?)email ?? DBNull.Value), DbHelper.P("@Id", userId));

    /*-------------- 会话（超时处理） --------------*/

    public void SaveSession(int userId, Guid token, DateTime time) =>
        DbHelper.ExecuteNonQuery(
            "UPDATE dbo.sys_user SET SessionToken=@T,SessionTime=@S WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@T", token), DbHelper.P("@S", time), DbHelper.P("@Id", userId));

    public (Guid Token, DateTime Time)? GetSession(int userId)
    {
        var t = DbHelper.GetTable("SELECT SessionToken,SessionTime FROM dbo.sys_user WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@Id", userId));
        if (t.Rows.Count == 0 || t.Rows[0]["SessionToken"] == DBNull.Value) return null;
        return (((Guid)t.Rows[0]["SessionToken"]), (DateTime)t.Rows[0]["SessionTime"]);
    }

    public void ClearSession(int userId) =>
        DbHelper.ExecuteNonQuery("UPDATE dbo.sys_user SET SessionToken=NULL,SessionTime=NULL WHERE UserId=@Id",
            CommandType.Text, DbHelper.P("@Id", userId));

    /*-------------- 日志 --------------*/

    public void WriteLoginLog(string userName, int? userId, bool success, string message, string ip) =>
        DbHelper.ExecuteNonQuery(
            @"INSERT INTO dbo.sys_login_log(UserName,UserId,Success,Message,Ip)
              VALUES(@U,@Id,@S,@M,@Ip)",
            CommandType.Text, DbHelper.P("@U", userName), DbHelper.P("@Id", (object?)userId ?? DBNull.Value),
            DbHelper.P("@S", success), DbHelper.P("@M", message ?? ""), DbHelper.P("@Ip", ip ?? ""));

    public void WriteOpLog(int userId, string userName, string module, string action, string? detail) =>
        DbHelper.ExecuteNonQuery(
            @"INSERT INTO dbo.sys_operation_log(UserId,UserName,Module,Action,Detail)
              VALUES(@Id,@U,@M,@A,@D)",
            CommandType.Text, DbHelper.P("@Id", userId), DbHelper.P("@U", userName),
            DbHelper.P("@M", module), DbHelper.P("@A", action), DbHelper.P("@D", (object?)detail ?? DBNull.Value));

    public DataTable ListLoginLogs(int top = 200) =>
        DbHelper.GetTable($"SELECT TOP ({top}) LoginLogId,UserName,Success,Message,Ip,LoginTime FROM dbo.sys_login_log ORDER BY LoginLogId DESC");

    public DataTable ListOpLogs(int top = 200) =>
        DbHelper.GetTable($"SELECT TOP ({top}) OpLogId,UserName,Module,Action,Detail,CreatedAt FROM dbo.sys_operation_log ORDER BY OpLogId DESC");
}
