using System.Data;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Xunit;

namespace IndustrialRepair.Tests;

/* ============ 假仓储：无数据库的服务级测试（R2） ============ */

public class FakeWorkOrderRepository : IWorkOrderRepository
{
    public WorkOrder? OrderToReturn;
    public int CompleteCalls, ReviewCalls, AssignCalls, CreateCalls;

    public string Create(int deviceId, int reporterId, string faultDesc, string? faultType,
                         string priority, string? photoPath, string? recognized, string? recognizedJson)
    { CreateCalls++; return "999"; }

    public void Assign(int orderId, int engineerId, int operatorId) => AssignCalls++;
    public void Take(int orderId, int engineerId) { }
    public void Complete(int orderId, int operatorId, string? summary) => CompleteCalls++;
    public void Cancel(int orderId, int operatorId, string reason) { }
    public void ReviewMaterials(int orderId, string mode, int? partId, decimal? baseQty, string? note, int operatorId)
        => ReviewCalls++;
    public DataTable Query(string? status, string? keyword, int? userId, bool onlyMine) => new();
    public WorkOrder? GetById(int orderId) => OrderToReturn;
    public List<WorkOrderRecord> GetRecords(int orderId) => new();
    public DataTable Report() => new();
}

public class FakePickingRepository : IPickingRepository
{
    public int GenerateCalls, IssueCalls;
    public int Generate(int orderId, string operatorName, decimal redundancy) { GenerateCalls++; return 1; }
    public void Issue(int pickingId, string operatorName) => IssueCalls++;
    public DataTable List(string? status, string? keyword) => new();
    public List<PickingItem> GetItems(int pickingId) => new();
}

/* ============ 工单服务守卫（无库） ============ */

/// <summary>AppSession 为进程级静态：守卫测试收进同一集合串行执行，避免并行互踩会话。</summary>
[Collection("Session")]
public class WorkOrderGuardTests : IDisposable
{
    private readonly FakeWorkOrderRepository _repo = new();
    private readonly WorkOrderService _svc;

    public WorkOrderGuardTests() => _svc = new WorkOrderService(_repo);

    private static UserInfo Login(string roleCode, params string[] perms)
    {
        var u = new UserInfo { UserId = 7, UserName = "tester", RealName = "测试者", RoleCode = roleCode };
        foreach (var p in perms) u.Permissions.Add(p);
        AppSession.Begin(u);
        return u;
    }

    public void Dispose() => AppSession.Logout();

    private static WorkOrder Order(int? engineerId = 7) => new()
    { OrderId = 1, EngineerId = engineerId, Status = "Working" };

    [Fact]
    public void 未登录派单被拒()
    {
        var r = _svc.Assign(1, 2, 7);
        Assert.False(r.Success);
        Assert.Contains("登录", r.Message);
        Assert.Equal(0, _repo.AssignCalls);
    }

    [Fact]
    public void 无权限派单被拒()
    {
        Login("Reporter", "order.view");
        var r = _svc.Assign(1, 2, 7);
        Assert.False(r.Success);
        Assert.Contains("order.assign", r.Message);
        Assert.Equal(0, _repo.AssignCalls);
    }

    [Fact]
    public void 操作者与会话不一致被拒()
    {
        Login("Admin", "order.assign");
        var r = _svc.Assign(1, 2, operatorId: 999);   // 假冒他人
        Assert.False(r.Success);
        Assert.Contains("不一致", r.Message);
    }

    [Fact]
    public void 工程师对非获派工单完工被拒()
    {
        var me = Login("Engineer", "order.handle", "order.view");
        _repo.OrderToReturn = Order(engineerId: 99);   // 派给别人
        var r = _svc.Complete(1, me.UserId, "总结");
        Assert.False(r.Success);
        Assert.Contains("获派", r.Message);
        Assert.Equal(0, _repo.CompleteCalls);
    }

    [Fact]
    public void 获派工程师完工通过并落库()
    {
        var me = Login("Engineer", "order.handle");
        _repo.OrderToReturn = Order(engineerId: me.UserId);
        var r = _svc.Complete(1, me.UserId, "总结");
        Assert.True(r.Success);
        Assert.Equal(1, _repo.CompleteCalls);
    }

    [Fact]
    public void 管理员可操作他人工单()
    {
        var admin = Login("Admin", "order.handle", "order.assign");
        _repo.OrderToReturn = Order(engineerId: 42);
        var r = _svc.Complete(1, admin.UserId, null);
        Assert.True(r.Success);
    }

    /* ---- 材料审阅（BL 级校验；业务规则在过程层另有冒烟覆盖）---- */

    [Fact]
    public void 审阅_无效模式被拒()
    {
        Login("Engineer", "order.handle");
        _repo.OrderToReturn = Order();
        var r = _svc.ReviewMaterials(new MaterialReviewRequest(1, "Whatever"));
        Assert.False(r.Success);
    }

    [Fact]
    public void 审阅_PartConfirmed缺零件被拒()
    {
        Login("Engineer", "order.handle");
        _repo.OrderToReturn = Order();
        var r = _svc.ReviewMaterials(new MaterialReviewRequest(1, MaterialReviewMode.PartConfirmed, PartId: null));
        Assert.False(r.Success);
        Assert.Contains("选择", r.Message);
    }

    [Fact]
    public void 审阅_PartConfirmed非正用量被拒()
    {
        Login("Engineer", "order.handle");
        _repo.OrderToReturn = Order();
        var r = _svc.ReviewMaterials(new MaterialReviewRequest(1, MaterialReviewMode.PartConfirmed, PartId: 5, BaseQty: 0m));
        Assert.False(r.Success);
        Assert.Contains("正数", r.Message);
        Assert.Equal(0, _repo.ReviewCalls);
    }

    [Fact]
    public void 审阅_获派工程师通过并落库()
    {
        var me = Login("Engineer", "order.handle");
        _repo.OrderToReturn = Order(engineerId: me.UserId);
        var r = _svc.ReviewMaterials(new MaterialReviewRequest(1, MaterialReviewMode.BomOnly, Note: "整机即可"));
        Assert.True(r.Success);
        Assert.Equal(1, _repo.ReviewCalls);
    }
}

/* ============ 领料服务守卫（无库） ============ */

[Collection("Session")]
public class PickingGuardTests : IDisposable
{
    private readonly FakePickingRepository _pick = new();
    private readonly FakeWorkOrderRepository _orders = new();
    private readonly PickingService _svc;

    public PickingGuardTests() => _svc = new PickingService(_pick, _orders);

    private static void Login(string roleCode, params string[] perms)
    {
        var u = new UserInfo { UserId = 7, UserName = "tester", RoleCode = roleCode };
        foreach (var p in perms) u.Permissions.Add(p);
        AppSession.Begin(u);
    }

    public void Dispose() => AppSession.Logout();

    [Fact]
    public void 未登录算料被拒()
    {
        var r = _svc.Generate(1, "tester", 1.10m);
        Assert.False(r.Success);
        Assert.Equal(0, _pick.GenerateCalls);
    }

    [Fact]
    public void 工程师对未获派工单算料被拒()
    {
        Login("Engineer", "picking.generate", "order.handle");
        _orders.OrderToReturn = new WorkOrder { OrderId = 1, EngineerId = 99, Status = "Working" };
        var r = _svc.Generate(1, "tester", 1.10m);
        Assert.False(r.Success);
        Assert.Contains("获派", r.Message);
    }

    [Fact]
    public void 库管员算料获派限制不适用_放行()
    {
        Login("Warehouse", "picking.generate", "picking.issue");   // 无 order.handle → 非工程师
        _orders.OrderToReturn = new WorkOrder { OrderId = 1, EngineerId = 99, Status = "Working" };
        var r = _svc.Generate(1, "tester", 1.10m);
        Assert.True(r.Success);
        Assert.Equal(1, _pick.GenerateCalls);
    }

    [Fact]
    public void 出库需picking_issue权限()
    {
        Login("Engineer", "picking.generate");   // 工程师无 picking.issue
        var r = _svc.Issue(1, "tester");
        Assert.False(r.Success);
        Assert.Contains("picking.issue", r.Message);
        Assert.Equal(0, _pick.IssueCalls);
    }

    [Fact]
    public void 冗余系数越界被拒()
    {
        Login("Warehouse", "picking.generate");
        var r = _svc.Generate(1, "tester", 3.0m);
        Assert.False(r.Success);
        Assert.Contains("冗余", r.Message);
        Assert.Equal(0, _pick.GenerateCalls);
    }
}
