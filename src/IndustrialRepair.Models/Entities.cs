using System;
using System.Collections.Generic;

namespace IndustrialRepair.Models;

/// <summary>统一业务返回结果</summary>
public class Result<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public T? Data { get; set; }
    public static Result<T> Ok(T data, string msg = "操作成功") => new() { Success = true, Data = data, Message = msg };
    public static Result<T> Fail(string msg) => new() { Success = false, Message = msg };
}

public class Result : Result<object?>
{
    public static Result Ok(string msg = "操作成功") => new() { Success = true, Message = msg };
    public static new Result Fail(string msg) => new() { Success = false, Message = msg };
}

/*==================== 系统权限（RBAC） ====================*/

public class RoleInfo
{
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string? Description { get; set; }
}

public class UserInfo
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string RealName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool MfaEnabled { get; set; }
    public string? MfaCode { get; set; }
    public bool Status { get; set; } = true;
    public int FailCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginTime { get; set; }
    /// <summary>登录后加载的权限代码集合</summary>
    public HashSet<string> Permissions { get; set; } = new();
    public bool HasPerm(string perm) => Permissions.Contains(perm);
}

/*==================== 配件库 ====================*/

public class PartCategory
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
}

public class Part
{
    public int PartId { get; set; }
    public string PartCode { get; set; } = "";
    public string PartName { get; set; } = "";
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string? Spec { get; set; }
    public string Unit { get; set; } = "个";
    public decimal Price { get; set; }
    public int StockQty { get; set; }
    public int MinStock { get; set; }
    /// <summary>拍照识别特征 {"color":[r,g,b],"shape":"circle","size":"small"}</summary>
    public string? FeatureJson { get; set; }
    public bool Status { get; set; } = true;
}

/*==================== 设备与 BOM ====================*/

public class Device
{
    public int DeviceId { get; set; }
    public string DeviceCode { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string? Location { get; set; }
    public bool Status { get; set; } = true;
}

public class BomItem
{
    public int BomId { get; set; }
    public int DeviceId { get; set; }
    public int PartId { get; set; }
    public string PartCode { get; set; } = "";
    public string PartName { get; set; } = "";
    public decimal QtyPer { get; set; }
    public string? Remark { get; set; }
}

/*==================== 工单 ====================*/

public class WorkOrder
{
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public int DeviceId { get; set; }
    public string DeviceCode { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string? Location { get; set; }
    public int ReporterId { get; set; }
    public string ReporterName { get; set; } = "";
    public int? EngineerId { get; set; }
    public string? EngineerName { get; set; }
    public string FaultDesc { get; set; } = "";
    public string? FaultType { get; set; }
    public string Priority { get; set; } = "Normal";
    public string? PhotoPath { get; set; }
    public string? Recognized { get; set; }
    public string? RecognizedJson { get; set; }        // 结构化识别候选（版本化文档，见 RecognizedDocument）
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? AssignTime { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? FinishTime { get; set; }
    public string? RepairSummary { get; set; }

    /*---- 材料审阅（M1）：未审阅时 MaterialReviewMode 为 null ----*/
    public string? MaterialReviewMode { get; set; }    // PartConfirmed / BomOnly
    public int? ConfirmedPartId { get; set; }
    public string? ConfirmedPartCode { get; set; }
    public string? ConfirmedPartName { get; set; }
    public decimal? ConfirmedBaseQty { get; set; }     // 仅 BOM 外补充件：人工基础用量
    public string? ReviewNote { get; set; }
    public int? ReviewedBy { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>材料审阅模式（与 usp_ReviewMaterials / 04 迁移约束一致）</summary>
public static class MaterialReviewMode
{
    public const string PartConfirmed = "PartConfirmed";   // 确认重点零件（BOM 内标记 / BOM 外人工用量追加）
    public const string BomOnly = "BomOnly";               // 仅按整机 BOM 配料

    public static string ToChinese(string? mode) => mode switch
    {
        PartConfirmed => "确认重点零件",
        BomOnly => "仅整机BOM",
        _ => "未审阅"
    };
}

/// <summary>材料审阅请求（工程师/管理员提交）</summary>
public record MaterialReviewRequest(
    int OrderId, string Mode, int? PartId = null,
    decimal? BaseQty = null, string? Note = null);

/// <summary>
/// 识别候选落库文档（work_order.RecognizedJson，版本 1）。
/// 保留引擎标识与候选明细；原始文本摘要仍在 Recognized 列，供旧工单回读兜底。
/// </summary>
public class RecognizedDocument
{
    public int Version { get; set; } = 1;
    public string Engine { get; set; } = "";
    public List<RecognizeResult> Candidates { get; set; } = new();
}

public class WorkOrderRecord
{
    public int RecordId { get; set; }
    public int OrderId { get; set; }
    public string Action { get; set; } = "";
    public string? Operator { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public static string ToChinese(string action) => action switch
    {
        "Create" => "报修创建", "Assign" => "派单", "Take" => "接单",
        "Finish" => "完工", "Cancel" => "取消", _ => action
    };
}

/*==================== 领料 ====================*/

public class PickingList
{
    public int PickingId { get; set; }
    public string PickingNo { get; set; } = "";
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string GeneratedBy { get; set; } = "";
    public decimal Redundancy { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
}

public class PickingItem
{
    public int ItemId { get; set; }
    public int PickingId { get; set; }
    public int PartId { get; set; }
    public string PartCode { get; set; } = "";
    public string PartName { get; set; } = "";
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? Remark { get; set; }
}

/*==================== 大屏统计 ====================*/

public class DashboardRow
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public int Val { get; set; }
}

/// <summary>拍照识别结果</summary>
public class RecognizeResult
{
    public int PartId { get; set; }
    public string PartCode { get; set; } = "";
    public string PartName { get; set; } = "";
    /// <summary>0~100 置信度</summary>
    public int Score { get; set; }
    public string Reason { get; set; } = "";
}
