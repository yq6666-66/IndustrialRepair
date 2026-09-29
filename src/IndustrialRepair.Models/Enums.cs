namespace IndustrialRepair.Models;

/// <summary>工单状态（存库代码，界面翻译）</summary>
public static class OrderStatusCode
{
    public const string Pending = "Pending";      // 待派单
    public const string Assigned = "Assigned";    // 已派单
    public const string Working = "Working";      // 维修中
    public const string Done = "Done";            // 已完成
    public const string Cancelled = "Cancelled";  // 已取消

    public static string ToChinese(string code) => code switch
    {
        Pending => "待派单", Assigned => "已派单", Working => "维修中",
        Done => "已完成", Cancelled => "已取消", _ => code
    };
}

/// <summary>优先级</summary>
public static class PriorityCode
{
    public const string Normal = "Normal";
    public const string Urgent = "Urgent";
    public const string Critical = "Critical";
    public const string Special = "Special";      // 特修

    public static string ToChinese(string code) => code switch
    {
        Normal => "一般", Urgent => "紧急", Critical => "严重", Special => "特修", _ => code
    };
}

/// <summary>领料单状态</summary>
public static class PickingStatusCode
{
    public const string Pending = "Pending";
    public const string Issued = "Issued";
    public const string Returned = "Returned";

    public static string ToChinese(string code) => code switch
    {
        Pending => "待出库", Issued => "已出库", Returned => "已退回", _ => code
    };
}
