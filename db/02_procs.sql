/*==============================================================
  工业维修智能化零件分配平台 —— 存储过程 / 触发器 / 视图（可重复执行）
  对应任务书要求：存储过程、事务(BeginTransaction/Commit/Rollback)、
  触发器(库存预警/流转记录)、视图(报表统计)、参数化防注入
==============================================================*/
SET NOCOUNT ON;
USE IndustrialRepairDb;
GO

/*==============================================================
  一、触发器
==============================================================*/

-- 1. 工单新建/状态变化自动写流转记录（维修历史记录由本表沉淀）
IF OBJECT_ID(N'dbo.trg_WorkOrder_Record', N'TR') IS NOT NULL DROP TRIGGER dbo.trg_WorkOrder_Record;
GO
CREATE TRIGGER dbo.trg_WorkOrder_Record
ON dbo.work_order
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(Status) OR EXISTS(SELECT 1 FROM inserted)
    BEGIN
        -- 新建
        INSERT INTO dbo.work_order_record(OrderId, Action, OperatorId, Operator, Note)
        SELECT i.OrderId, N'Create', i.ReporterId,
               ISNULL((SELECT UserName FROM dbo.sys_user WHERE UserId = i.ReporterId), N'-'),
               N'报修创建：' + ISNULL(i.FaultDesc, N'')
        FROM inserted i
        WHERE NOT EXISTS(SELECT 1 FROM dbo.work_order o WHERE o.OrderId = i.OrderId AND o.Status <> N'Pending');

        -- 状态流转（排除新建本身）
        INSERT INTO dbo.work_order_record(OrderId, Action, OperatorId, Operator, Note)
        SELECT i.OrderId,
               CASE i.Status
                    WHEN N'Assigned' THEN N'Assign'
                    WHEN N'Working'  THEN N'Take'
                    WHEN N'Done'     THEN N'Finish'
                    WHEN N'Cancelled'THEN N'Cancel'
                    ELSE i.Status END,
               ISNULL(i.EngineerId, i.ReporterId),
               ISNULL((SELECT UserName FROM dbo.sys_user u WHERE u.UserId = ISNULL(i.EngineerId, i.ReporterId)), N'-'),
               CASE i.Status WHEN N'Done' THEN ISNULL(i.RepairSummary, N'维修完成') ELSE N'状态 → ' + i.Status END
        FROM inserted i
        JOIN deleted d ON d.OrderId = i.OrderId
        WHERE ISNULL(i.Status, N'') <> ISNULL(d.Status, N'');
    END
END
GO

-- 2. 配件库存更新触发预警（StockQty < MinStock 写 stock_alert）
IF OBJECT_ID(N'dbo.trg_Part_StockAlert', N'TR') IS NOT NULL DROP TRIGGER dbo.trg_Part_StockAlert;
GO
CREATE TRIGGER dbo.trg_Part_StockAlert
ON dbo.part
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(StockQty) RETURN;
    INSERT INTO dbo.stock_alert(PartId, StockQty, MinStock)
    SELECT i.PartId, i.StockQty, i.MinStock
    FROM inserted i
    JOIN deleted d ON d.PartId = i.PartId
    WHERE i.StockQty < i.MinStock                    -- 低于下限才预警
      AND d.StockQty >= d.MinStock                   -- 只在“跌破”那一刻记录一次
      AND NOT EXISTS(SELECT 1 FROM dbo.stock_alert a
                     WHERE a.PartId = i.PartId AND a.Handled = 0); -- 未处理的预警不重复
END
GO

-- 3. 领料明细生成时同步库存流水（预占视角：负变更在出库时记录，此处记生成参考）
--    说明：正式出库扣减在 usp_IssuePicking 事务内完成，这里保证“任何明细插入必留痕”
IF OBJECT_ID(N'dbo.trg_PickingItem_Trace', N'TR') IS NOT NULL DROP TRIGGER dbo.trg_PickingItem_Trace;
GO
CREATE TRIGGER dbo.trg_PickingItem_Trace
ON dbo.picking_item
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.part_stock_log(PartId, ChangeQty, AfterQty, RefNo)
    SELECT i.PartId, 0, p.StockQty, N'GEN:' + pl.PickingNo
    FROM inserted i
    JOIN dbo.part p ON p.PartId = i.PartId
    JOIN dbo.picking_list pl ON pl.PickingId = i.PickingId;
END
GO

/*==============================================================
  二、存储过程（内部全部使用参数，杜绝 SQL 注入）
==============================================================*/

-- 1. 创建报修工单（生成规则单号）
IF OBJECT_ID(N'dbo.usp_CreateOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_CreateOrder;
GO
CREATE PROCEDURE dbo.usp_CreateOrder
    @DeviceId   INT,
    @ReporterId INT,
    @FaultDesc  NVARCHAR(400),
    @FaultType  NVARCHAR(30),
    @Priority   NVARCHAR(10),
    @PhotoPath  NVARCHAR(260),
    @Recognized NVARCHAR(400),
    @NewOrderId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @No NVARCHAR(30), @Seq INT;
    SELECT @Seq = ISNULL(MAX(RIGHT(OrderNo, 4)), 0) + 1
      FROM dbo.work_order
     WHERE CONVERT(NVARCHAR(8), CreatedAt, 112) = CONVERT(NVARCHAR(8), GETDATE(), 112);
    SET @No = N'WO' + CONVERT(NVARCHAR(8), GETDATE(), 112) + RIGHT(N'000' + CAST(@Seq AS NVARCHAR(10)), 4);
    INSERT INTO dbo.work_order(OrderNo, DeviceId, ReporterId, FaultDesc, FaultType, Priority, PhotoPath, Recognized, Status)
    VALUES(@No, @DeviceId, @ReporterId, @FaultDesc, @FaultType, @Priority, @PhotoPath, @Recognized, N'Pending');
    SET @NewOrderId = SCOPE_IDENTITY();
END
GO

-- 2. 派单（管理员/调度 指派维修工程师）
IF OBJECT_ID(N'dbo.usp_AssignOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_AssignOrder;
GO
CREATE PROCEDURE dbo.usp_AssignOrder
    @OrderId     INT,
    @EngineerId  INT,
    @OperatorId  INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.work_order
       SET Status = N'Assigned', EngineerId = @EngineerId, AssignTime = GETDATE()
     WHERE OrderId = @OrderId AND Status = N'Pending';
    IF @@ROWCOUNT = 0
        RAISERROR(N'派单失败：工单不存在或当前状态不允许派单。', 16, 1);
END
GO

-- 3. 接单开工
IF OBJECT_ID(N'dbo.usp_TakeOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_TakeOrder;
GO
CREATE PROCEDURE dbo.usp_TakeOrder
    @OrderId    INT,
    @EngineerId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.work_order
       SET Status = N'Working', StartTime = GETDATE()
     WHERE OrderId = @OrderId AND EngineerId = @EngineerId AND Status = N'Assigned';
    IF @@ROWCOUNT = 0
        RAISERROR(N'接单失败：工单未指派给该工程师或状态不允许接单。', 16, 1);
END
GO

-- 4. 完工
IF OBJECT_ID(N'dbo.usp_CompleteOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_CompleteOrder;
GO
CREATE PROCEDURE dbo.usp_CompleteOrder
    @OrderId    INT,
    @OperatorId INT,
    @Summary    NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.work_order
       SET Status = N'Done', FinishTime = GETDATE(), RepairSummary = ISNULL(@Summary, N'')
     WHERE OrderId = @OrderId AND Status IN (N'Working', N'Assigned');
    IF @@ROWCOUNT = 0
        RAISERROR(N'完工失败：工单状态不允许完工。', 16, 1);
END
GO

-- 5. 取消工单
IF OBJECT_ID(N'dbo.usp_CancelOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_CancelOrder;
GO
CREATE PROCEDURE dbo.usp_CancelOrder
    @OrderId    INT,
    @OperatorId INT,
    @Reason     NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.work_order SET Status = N'Cancelled'
     WHERE OrderId = @OrderId AND Status IN (N'Pending', N'Assigned');
    IF @@ROWCOUNT = 0
        RAISERROR(N'取消失败：工单已在维修中或已完成。', 16, 1);
END
GO

-- 6. 智能算料：BOM 匹配生成领料单（含冗余系数），事务保证原子性
IF OBJECT_ID(N'dbo.usp_GeneratePicking', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GeneratePicking;
GO
CREATE PROCEDURE dbo.usp_GeneratePicking
    @OrderId     INT,
    @Operator    NVARCHAR(30),
    @Redundancy  DECIMAL(4,2) = 1.10,     -- 冗余系数：留有一定冗余
    @NewPickingId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;                    -- 任意错误自动回滚
    BEGIN TRAN;                           -- BeginTransaction
    BEGIN TRY
        IF NOT EXISTS(SELECT 1 FROM dbo.work_order WHERE OrderId = @OrderId AND Status IN (N'Assigned', N'Working'))
        BEGIN
            RAISERROR(N'算料失败：仅已派单/维修中的工单可生成领料单。', 16, 1);
        END
        IF EXISTS(SELECT 1 FROM dbo.picking_list WHERE OrderId = @OrderId AND Status = N'Pending')
        BEGIN
            RAISERROR(N'算料失败：该工单已存在未出库的领料单。', 16, 1);
        END

        DECLARE @No NVARCHAR(30), @Seq INT, @DevId INT;
        SELECT @Seq = ISNULL(MAX(RIGHT(PickingNo, 4)), 0) + 1
          FROM dbo.picking_list
         WHERE CONVERT(NVARCHAR(8), CreatedAt, 112) = CONVERT(NVARCHAR(8), GETDATE(), 112);
        SET @No = N'PK' + CONVERT(NVARCHAR(8), GETDATE(), 112) + RIGHT(N'000' + CAST(@Seq AS NVARCHAR(10)), 4);
        SELECT @DevId = DeviceId FROM dbo.work_order WHERE OrderId = @OrderId;

        INSERT INTO dbo.picking_list(PickingNo, OrderId, GeneratedBy, Redundancy)
        VALUES(@No, @OrderId, @Operator, @Redundancy);
        SET @NewPickingId = SCOPE_IDENTITY();

        -- BOM 用量 × 冗余系数 → 向上取整 → 生成明细（同件自动合并）
        ;WITH need AS (
            SELECT b.PartId, CEILING(b.QtyPer * @Redundancy) AS Qty
            FROM dbo.bom b WHERE b.DeviceId = @DevId
        )
        INSERT INTO dbo.picking_item(PickingId, PartId, PartCode, PartName, Qty, UnitPrice, Remark)
        SELECT @NewPickingId, n.PartId, p.PartCode, p.PartName, n.Qty, p.Price,
               N'单台用量×冗余' + CAST(@Redundancy AS NVARCHAR(6))
        FROM need n JOIN dbo.part p ON p.PartId = n.PartId;

        IF NOT EXISTS(SELECT 1 FROM dbo.picking_item WHERE PickingId = @NewPickingId)
            RAISERROR(N'算料失败：该设备未维护 BOM，无法推算材料。', 16, 1);

        UPDATE dbo.picking_list
           SET TotalAmount = (SELECT ISNULL(SUM(Amount), 0) FROM dbo.picking_item WHERE PickingId = @NewPickingId)
         WHERE PickingId = @NewPickingId;

        COMMIT TRAN;                      -- Commit
    END TRY
    BEGIN CATCH
        ROLLBACK TRAN;                    -- Rollback
        THROW;                            -- 向上层抛出 SqlException，由客户端捕获提示
    END CATCH
END
GO

-- 7. 领料出库：事务扣减库存 + 写流水 + 处理预警；库存不足回滚
IF OBJECT_ID(N'dbo.usp_IssuePicking', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_IssuePicking;
GO
CREATE PROCEDURE dbo.usp_IssuePicking
    @PickingId INT,
    @Operator  NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    BEGIN TRY
        IF NOT EXISTS(SELECT 1 FROM dbo.picking_list WHERE PickingId = @PickingId AND Status = N'Pending')
            RAISERROR(N'出库失败：领料单不存在或已出库。', 16, 1);

        -- 库存不足检查（触发约束回滚，演示 Rollback）
        IF EXISTS(SELECT 1
                  FROM dbo.picking_item i JOIN dbo.part p ON p.PartId = i.PartId
                  WHERE i.PickingId = @PickingId AND p.StockQty < i.Qty)
        BEGIN
            DECLARE @lack NVARCHAR(200);
            SELECT TOP 1 @lack = p.PartName + N'（库存' + CAST(p.StockQty AS NVARCHAR(10))
                               + N'，需' + CAST(i.Qty AS NVARCHAR(10)) + N'）'
            FROM dbo.picking_item i JOIN dbo.part p ON p.PartId = i.PartId
            WHERE i.PickingId = @PickingId AND p.StockQty < i.Qty;
            RAISERROR(N'出库失败：库存不足 - %s', 16, 1, @lack);
        END

        -- 扣减库存 + 流水
        UPDATE p
           SET p.StockQty = p.StockQty - i.Qty
        FROM dbo.part p
        JOIN dbo.picking_item i ON i.PartId = p.PartId AND i.PickingId = @PickingId;

        INSERT INTO dbo.part_stock_log(PartId, ChangeQty, AfterQty, RefNo)
        SELECT i.PartId, -i.Qty, p.StockQty, pl.PickingNo
        FROM dbo.picking_item i
        JOIN dbo.part p ON p.PartId = i.PartId
        JOIN dbo.picking_list pl ON pl.PickingId = i.PickingId
        WHERE i.PickingId = @PickingId;

        -- 触发预警的配件标记处理（出库后仍低于下限的保持未处理）
        UPDATE dbo.picking_list SET Status = N'Issued', IssuedAt = GETDATE()
         WHERE PickingId = @PickingId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

-- 8. 配件入库（含流水）
IF OBJECT_ID(N'dbo.usp_StockIn', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_StockIn;
GO
CREATE PROCEDURE dbo.usp_StockIn
    @PartId INT,
    @Qty    INT,
    @RefNo  NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRAN;
    BEGIN TRY
        UPDATE dbo.part SET StockQty = StockQty + @Qty WHERE PartId = @PartId;
        IF @@ROWCOUNT = 0 RAISERROR(N'入库失败：配件不存在。', 16, 1);
        INSERT INTO dbo.part_stock_log(PartId, ChangeQty, AfterQty, RefNo)
        SELECT PartId, @Qty, StockQty, @RefNo FROM dbo.part WHERE PartId = @PartId;
        -- 入库回到安全线以上时，关闭未处理预警
        UPDATE a SET a.Handled = 1
        FROM dbo.stock_alert a JOIN dbo.part p ON p.PartId = a.PartId
        WHERE a.PartId = @PartId AND a.Handled = 0 AND p.StockQty >= p.MinStock;
        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        ROLLBACK TRAN; THROW;
    END CATCH
END
GO

-- 9. 大屏统计：状态分布 / 近7日趋势 / 工程师排行 / 低库存
IF OBJECT_ID(N'dbo.usp_GetDashboard', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetDashboard;
GO
CREATE PROCEDURE dbo.usp_GetDashboard
AS
BEGIN
    SET NOCOUNT ON;
    SELECT N'状态分布' AS Kind, Status AS Label, COUNT(*) AS Val FROM dbo.work_order GROUP BY Status
    UNION ALL
    SELECT N'近7日工单', CONVERT(NVARCHAR(10), d.Day, 120), ISNULL(c.Total, 0)
    FROM (SELECT DISTINCT CONVERT(date, CreatedAt) AS Day FROM dbo.work_order
          WHERE CreatedAt >= DATEADD(DAY, -6, CONVERT(date, GETDATE()))) d
    OUTER APPLY (SELECT COUNT(*) Total FROM dbo.work_order o
                 WHERE CONVERT(date, o.CreatedAt) = d.Day) c
    UNION ALL
    SELECT N'工程师完工', u.RealName, COUNT(*) FROM dbo.work_order o
    JOIN dbo.sys_user u ON u.UserId = o.EngineerId
    WHERE o.Status = N'Done' GROUP BY u.RealName
    UNION ALL
    SELECT N'库存预警', p.PartName, p.StockQty FROM dbo.part p
    WHERE p.StockQty < p.MinStock;
END
GO

/*==============================================================
  三、视图（报表统计 / 模糊检索数据源）
==============================================================*/
IF OBJECT_ID(N'dbo.vw_WorkOrder_Full', N'V') IS NOT NULL DROP VIEW dbo.vw_WorkOrder_Full;
GO
CREATE VIEW dbo.vw_WorkOrder_Full
AS
SELECT o.OrderId, o.OrderNo, o.Status, o.Priority, o.FaultType, o.FaultDesc,
       o.PhotoPath, o.Recognized, o.CreatedAt, o.AssignTime, o.StartTime, o.FinishTime, o.RepairSummary,
       o.ReporterId, o.EngineerId,
       d.DeviceCode, d.DeviceName, d.DeviceId,
       rp.RealName AS ReporterName, eg.RealName AS EngineerName
FROM dbo.work_order o
JOIN dbo.device d ON d.DeviceId = o.DeviceId
JOIN dbo.sys_user rp ON rp.UserId = o.ReporterId
LEFT JOIN dbo.sys_user eg ON eg.UserId = o.EngineerId;
GO

IF OBJECT_ID(N'dbo.vw_RepairReport', N'V') IS NOT NULL DROP VIEW dbo.vw_RepairReport;
GO
CREATE VIEW dbo.vw_RepairReport
AS
WITH cost AS (
    SELECT pl.OrderId, SUM(i.Qty * i.UnitPrice) AS MaterialCost
    FROM dbo.picking_list pl
    JOIN dbo.picking_item i ON i.PickingId = pl.PickingId
    GROUP BY pl.OrderId
)
SELECT CONVERT(NVARCHAR(7), o.CreatedAt, 120) AS [月份],
       COUNT(*) AS [工单数],
       SUM(CASE WHEN o.Status = N'Done' THEN 1 ELSE 0 END) AS [完成数],
       SUM(CASE WHEN o.Status = N'Cancelled' THEN 1 ELSE 0 END) AS [取消数],
       AVG(CASE WHEN o.FinishTime IS NULL THEN NULL
                ELSE DATEDIFF(MINUTE, o.CreatedAt, o.FinishTime) END) AS [平均处理分钟],
       SUM(ISNULL(c.MaterialCost, 0)) AS [材料成本]
FROM dbo.work_order o
LEFT JOIN cost c ON c.OrderId = o.OrderId
GROUP BY CONVERT(NVARCHAR(7), o.CreatedAt, 120);
GO

IF OBJECT_ID(N'dbo.vw_LowStock', N'V') IS NOT NULL DROP VIEW dbo.vw_LowStock;
GO
CREATE VIEW dbo.vw_LowStock
AS
SELECT p.PartId, p.PartCode, p.PartName, p.Spec, p.Unit, p.StockQty, p.MinStock,
       c.CategoryName, (p.MinStock - p.StockQty) AS Gap
FROM dbo.part p JOIN dbo.part_category c ON c.CategoryId = p.CategoryId
WHERE p.StockQty < p.MinStock;
GO

IF OBJECT_ID(N'dbo.vw_PickingDetail', N'V') IS NOT NULL DROP VIEW dbo.vw_PickingDetail;
GO
CREATE VIEW dbo.vw_PickingDetail
AS
SELECT pl.PickingId, pl.PickingNo, pl.Status AS Status, pl.Redundancy, pl.TotalAmount,
       pl.CreatedAt, pl.IssuedAt, pl.GeneratedBy,
       o.OrderNo, o.OrderId, d.DeviceName,
       i.ItemId, i.PartId, i.PartCode, i.PartName, i.Qty, i.UnitPrice, i.Amount, i.Remark
FROM dbo.picking_list pl
JOIN dbo.work_order o ON o.OrderId = pl.OrderId
JOIN dbo.device d ON d.DeviceId = o.DeviceId
LEFT JOIN dbo.picking_item i ON i.PickingId = pl.PickingId;
GO

PRINT N'02_procs.sql 执行完成：存储过程 / 触发器 / 视图就绪。';
GO
