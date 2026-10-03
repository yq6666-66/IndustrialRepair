using System.Data;
using IndustrialRepair.Models;

namespace IndustrialRepair.DAL;

/// <summary>
/// 工单仓储契约（R2 可测试性：BLL 服务构造注入，单测用假实现替代数据库）。
/// 成员与 WorkOrderRepository 公共方法一致；新增能力请先扩展本接口。
/// </summary>
public interface IWorkOrderRepository
{
    string Create(int deviceId, int reporterId, string faultDesc, string? faultType,
                  string priority, string? photoPath, string? recognized, string? recognizedJson);
    void Assign(int orderId, int engineerId, int operatorId);
    void Take(int orderId, int engineerId);
    void Complete(int orderId, int operatorId, string? summary);
    void Cancel(int orderId, int operatorId, string reason);
    void ReviewMaterials(int orderId, string mode, int? partId, decimal? baseQty,
                         string? note, int operatorId);
    DataTable Query(string? status, string? keyword, int? userId, bool onlyMine);
    WorkOrder? GetById(int orderId);
    List<WorkOrderRecord> GetRecords(int orderId);
    DataTable Report();
}

/// <summary>领料仓储契约（同上，供 PickingService 注入）。</summary>
public interface IPickingRepository
{
    int Generate(int orderId, string operatorName, decimal redundancy);
    void Issue(int pickingId, string operatorName);
    DataTable List(string? status, string? keyword);
    List<PickingItem> GetItems(int pickingId);
}
