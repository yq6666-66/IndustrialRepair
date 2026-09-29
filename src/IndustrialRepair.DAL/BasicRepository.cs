using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>设备 / BOM / 大屏 / 环境自检</summary>
public class BasicRepository
{
    /*-------------- 设备台账 --------------*/

    public List<Device> ListDevices(string? keyword = null)
    {
        var list = new List<Device>();
        var t = DbHelper.GetTable(
            @"SELECT DeviceId,DeviceCode,DeviceName,Location,Status FROM dbo.device
              WHERE (@KW IS NULL OR DeviceCode LIKE @KWL OR DeviceName LIKE @KWL OR Location LIKE @KWL)
              ORDER BY DeviceCode",
            CommandType.Text,
            DbHelper.P("@KW", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword),
            DbHelper.P("@KWL", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : $"%{keyword}%"));
        foreach (DataRow r in t.Rows)
            list.Add(new Device
            {
                DeviceId = (int)r["DeviceId"], DeviceCode = r["DeviceCode"].ToString()!,
                DeviceName = r["DeviceName"].ToString()!,
                Location = r["Location"] == DBNull.Value ? null : r["Location"].ToString(),
                Status = (bool)r["Status"]
            });
        return list;
    }

    public void SaveDevice(Device d)
    {
        if (d.DeviceId == 0)
            DbHelper.ExecuteNonQuery(
                @"INSERT INTO dbo.device(DeviceCode,DeviceName,Location,Status) VALUES(@C,@N,@L,@S)",
                CommandType.Text, DbHelper.P("@C", d.DeviceCode), DbHelper.P("@N", d.DeviceName),
                DbHelper.P("@L", (object?)d.Location ?? DBNull.Value), DbHelper.P("@S", d.Status));
        else
            DbHelper.ExecuteNonQuery(
                @"UPDATE dbo.device SET DeviceCode=@C,DeviceName=@N,Location=@L,Status=@S WHERE DeviceId=@Id",
                CommandType.Text, DbHelper.P("@C", d.DeviceCode), DbHelper.P("@N", d.DeviceName),
                DbHelper.P("@L", (object?)d.Location ?? DBNull.Value), DbHelper.P("@S", d.Status),
                DbHelper.P("@Id", d.DeviceId));
    }

    public void DeleteDevice(int deviceId) =>
        DbHelper.ExecuteNonQuery("DELETE FROM dbo.device WHERE DeviceId=@Id", CommandType.Text, DbHelper.P("@Id", deviceId));

    /*-------------- BOM --------------*/

    public List<BomItem> ListBom(int deviceId)
    {
        var list = new List<BomItem>();
        var t = DbHelper.GetTable(
            @"SELECT b.BomId,b.DeviceId,b.PartId,p.PartCode,p.PartName,b.QtyPer,b.Remark
              FROM dbo.bom b JOIN dbo.part p ON p.PartId=b.PartId
              WHERE b.DeviceId=@D ORDER BY p.PartCode",
            CommandType.Text, DbHelper.P("@D", deviceId));
        foreach (DataRow r in t.Rows)
            list.Add(new BomItem
            {
                BomId = (int)r["BomId"], DeviceId = (int)r["DeviceId"], PartId = (int)r["PartId"],
                PartCode = r["PartCode"].ToString()!, PartName = r["PartName"].ToString()!,
                QtyPer = (decimal)r["QtyPer"],
                Remark = r["Remark"] == DBNull.Value ? null : r["Remark"].ToString()
            });
        return list;
    }

    public void AddBom(int deviceId, int partId, decimal qtyPer, string? remark) =>
        DbHelper.ExecuteNonQuery(
            @"INSERT INTO dbo.bom(DeviceId,PartId,QtyPer,Remark) VALUES(@D,@P,@Q,@R)",
            CommandType.Text, DbHelper.P("@D", deviceId), DbHelper.P("@P", partId), DbHelper.P("@Q", qtyPer),
            DbHelper.P("@R", (object?)remark ?? DBNull.Value));

    public void UpdateBomQty(int bomId, decimal qtyPer) =>
        DbHelper.ExecuteNonQuery("UPDATE dbo.bom SET QtyPer=@Q WHERE BomId=@Id",
            CommandType.Text, DbHelper.P("@Q", qtyPer), DbHelper.P("@Id", bomId));

    public void DeleteBom(int bomId) =>
        DbHelper.ExecuteNonQuery("DELETE FROM dbo.bom WHERE BomId=@Id", CommandType.Text, DbHelper.P("@Id", bomId));

    /*-------------- 大屏统计 --------------*/

    public List<DashboardRow> GetDashboard()
    {
        var list = new List<DashboardRow>();
        var t = DbHelper.GetTable("EXEC dbo.usp_GetDashboard");
        foreach (DataRow r in t.Rows)
            list.Add(new DashboardRow
            {
                Kind = r["Kind"].ToString()!,
                Label = r["Label"].ToString()!,
                Val = Convert.ToInt32(r["Val"])
            });
        return list;
    }

    /*-------------- 环境自检 --------------*/

    /// <summary>数据库是否已初始化（核心表存在且有角色数据）</summary>
    public bool IsDatabaseReady()
    {
        try
        {
            var v = DbHelper.ExecuteScalar(
                "SELECT CASE WHEN OBJECT_ID('dbo.sys_role') IS NULL THEN 0 ELSE 1 END");
            return Convert.ToInt32(v ?? 0) == 1;
        }
        catch { return false; }
    }

    public bool HasUsers() =>
        Convert.ToInt32(DbHelper.ExecuteScalar("SELECT COUNT(*) FROM dbo.sys_user") ?? 0) > 0;
}
