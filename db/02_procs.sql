/*==============================================================
  工业维修智能化零件分配平台 —— 存储过程 / 触发器 / 视图
  对应任务书要求：存储过程、事务(BeginTransaction/Commit/Rollback)、
  触发器(库存预警/流转记录)、视图(报表统计)、参数化防注入
  维护方式：全部 CREATE OR ALTER（幂等）；由迁移运行器按文件哈希刷新，
  也可用 sqlcmd 手工重跑（注意先 USE 目标库）。
  M0 修订：
    - trg_WorkOrder_Record：Create 仅记真正新增行；状态记录仅在状态真实变化时
    - usp_CreateOrder/usp_GeneratePicking：sp_getapplock 串行分配单号（并发安全）
    - usp_GeneratePicking/usp_IssuePicking：UPDLOCK 锁定工单与领料单，并发重复生成/出库只成功一次
    - 唯一过滤索引 UQ_picking_pending_per_order 兜底（04 迁移创建）
==============================================================*/
SET NOCOUNT ON;
USE IndustrialRepairDb;
GO

/*==============================================================
  一、触发器
==============================================================*/

-- 1. 工单新建/状态变化自动写流转记录（维修历史记录由本表沉淀）
--    Create 只针对真正新增（inserted 不在 deleted 中）；
--    状态记录要求旧、新状态不同——普通字段更新（如人工审阅）不产生流转记录。
CREATE OR ALTER TRIGGER dbo.trg_WorkOrder_Record
ON dbo.work_order
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- 真正的新增行（非更新）
    INSERT INTO dbo.work_order_record(OrderId, Action, OperatorId, Operator, Note)
    SELECT i.OrderId, N'Create', i.ReporterId,
           ISNULL((SELECT UserName FROM dbo.sys_user WHERE UserId = i.ReporterId), N'-'),
           N'报修创建：' + ISNULL(i.FaultDesc, N'')
    FROM inserted i
    WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.OrderId = i.OrderId);

    -- 状态真实变化的更新行（含 Assign/Take/Finish/Cancel）
    INSERT INTO dbo.work_order_record(OrderId, Action, OperatorId, Operator, Note)
    SELECT i.OrderId,
           CASE i.Status
                WHEN N'Assigned'  THEN N'Assign'
                WHEN N'Working'   THEN N'Take'
                WHEN N'Done'      THEN N'Finish'
                WHEN N'Cancelled' THEN N'Cancel'
                ELSE i.Status END,
           ISNULL(i.EngineerId, i.ReporterId),
           ISNULL((SELECT UserName FROM dbo.sys_user u WHERE u.UserId = ISNULL(i.EngineerId, i.ReporterId)), N'-'),
           CASE i.Status WHEN N'Done' THEN ISNULL(i.RepairSummary, N'维修完成') ELSE N'状态 → ' + i.Status END
    FROM inserted i
    JOIN deleted d ON d.OrderId = i.OrderId
    WHERE ISNULL(i.Status, N'') <> ISNULL(d.Status, N'');
END
GO

-- 2. 配件库存更新触发预警（StockQty < MinStock 写 stock_alert）
CREATE OR ALTER TRIGGER dbo.trg_Part_StockAlert
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
CREATE OR ALTER TRIGGER dbo.trg_PickingItem_Trace
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

-- 1. 创建报修工单（applock 串行分配单号；@RecognizedJson 为结构化识别候选，可空）
CREATE OR ALTER PROCEDURE dbo.usp_CreateOrder
    @DeviceId       INT,
    @ReporterId     INT,
    @FaultDesc      NVARCHAR(400),
    @FaultType      NVARCHAR(30),
    @Priority       NVARCHAR(10),
    @PhotoPath      NVARCHAR(260),
    @Recognized     NVARCHAR(400),
    @NewOrderId     INT OUTPUT,
    @RecognizedJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    BEGIN TRY
        DECLARE @No NVARCHAR(30), @Seq INT, @lr INT, @Res NVARCHAR(60);
        SET @Res = N'SEQ_WorkOrder_' + CONVERT(NVARCHAR(8), GETDATE(), 112);
        EXEC @lr = dbo.sp_getapplock
             @Resource = @Res,
             @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
        IF @lr < 0 RAISERROR(N'工单号分配繁忙，请重试。', 16, 1);

        SELECT @Seq = ISNULL(MAX(RIGHT(OrderNo, 4)), 0) + 1
          FROM dbo.work_order
         WHERE CONVERT(NVARCHAR(8), CreatedAt, 112) = CONVERT(NVARCHAR(8), GETDATE(), 112);
        SET @No = N'WO' + CONVERT(NVARCHAR(8), GETDATE(), 112) + RIGHT(N'000' + CAST(@Seq AS NVARCHAR(10)), 4);

        INSERT INTO dbo.work_order(OrderNo, DeviceId, ReporterId, FaultDesc, FaultType, Priority,
                                   PhotoPath, Recognized, RecognizedJson, Status)
        VALUES(@No, @DeviceId, @ReporterId, @FaultDesc, @FaultType, @Priority,
               @PhotoPath, @Recognized, @RecognizedJson, N'Pending');
        SET @NewOrderId = SCOPE_IDENTITY();
        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

-- 2. 派单（管理员/调度 指派维修工程师）
CREATE OR ALTER PROCEDURE dbo.usp_AssignOrder
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
CREATE OR ALTER PROCEDURE dbo.usp_TakeOrder
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
CREATE OR ALTER PROCEDURE dbo.usp_CompleteOrder
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
CREATE OR ALTER PROCEDURE dbo.usp_CancelOrder
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

-- 6a. 材料审阅：人工确认重点零件 / 仅整机 BOM（与审阅记录同一事务）
--     规则：仅 Assigned/Working 工单可审阅；存在未出库领料单时禁止更改；
--     BOM 内确认件不允许覆盖 BOM 用量；BOM 外补充件必须人工正用量+理由；BomOnly 必填理由。
CREATE OR ALTER PROCEDURE dbo.usp_ReviewMaterials
    @OrderId    INT,
    @Mode       NVARCHAR(20),            -- PartConfirmed / BomOnly
    @PartId     INT           = NULL,
    @BaseQty    DECIMAL(10,2) = NULL,    -- 仅 BOM 外补充件：人工基础用量
    @Note       NVARCHAR(200) = NULL,
    @OperatorId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    BEGIN TRY
        IF @Mode NOT IN (N'PartConfirmed', N'BomOnly')
            RAISERROR(N'审阅失败：审阅模式无效。', 16, 1);

        DECLARE @St NVARCHAR(20), @DevId INT;
        SELECT @St = Status, @DevId = DeviceId
          FROM dbo.work_order WITH (UPDLOCK, HOLDLOCK)
         WHERE OrderId = @OrderId;
        IF @St IS NULL
            RAISERROR(N'审阅失败：工单不存在。', 16, 1);
        IF @St NOT IN (N'Assigned', N'Working')
            RAISERROR(N'审阅失败：仅已派单/维修中的工单可审阅（完成/取消工单仅查看）。', 16, 1);

        IF EXISTS(SELECT 1 FROM dbo.picking_list WHERE OrderId = @OrderId AND Status = N'Pending')
            RAISERROR(N'审阅失败：该工单存在未出库领料单，不能更改审阅（避免已生成清单与确认结果不一致）。', 16, 1);

        IF @Mode = N'PartConfirmed'
        BEGIN
            DECLARE @PStatus BIT, @InBom BIT = 0;
            SELECT @PStatus = Status FROM dbo.part WHERE PartId = @PartId;
            IF @PStatus IS NULL
                RAISERROR(N'审阅失败：零件不存在。', 16, 1);
            IF @PStatus = 0
                RAISERROR(N'审阅失败：零件已停用，不能确认为维修重点。', 16, 1);

            IF EXISTS(SELECT 1 FROM dbo.bom WHERE DeviceId = @DevId AND PartId = @PartId)
            BEGIN
                SET @InBom = 1;
                IF @BaseQty IS NOT NULL
                    RAISERROR(N'审阅失败：该零件属于设备 BOM，按 BOM 既有用量配料，不允许人工覆盖用量。', 16, 1);
            END
            ELSE IF (@BaseQty IS NULL OR @BaseQty <= 0)
                RAISERROR(N'审阅失败：BOM 外补充件必须填写正的人工基础用量（用量为人工指定，非模型推算）。', 16, 1);

            IF @InBom = 0 AND ISNULL(@Note, N'') = N''
                RAISERROR(N'审阅失败：BOM 外补充件必须填写理由。', 16, 1);
        END
        ELSE IF ISNULL(@Note, N'') = N''
            RAISERROR(N'审阅失败：选择“仅按整机 BOM 配料”必须填写理由。', 16, 1);

        IF @Mode = N'BomOnly' SET @PartId = NULL;   -- BomOnly 不绑定零件

        UPDATE dbo.work_order
           SET MaterialReviewMode = @Mode,
               ConfirmedPartId    = @PartId,
               ConfirmedBaseQty   = @BaseQty,
               ReviewNote         = @Note,
               ReviewedBy         = @OperatorId,
               ReviewedAt         = GETDATE()
         WHERE OrderId = @OrderId;

        -- 审阅留痕（触发器只记状态变化，审阅记录在此显式写入同一事务）
        INSERT INTO dbo.work_order_record(OrderId, Action, OperatorId, Operator, Note)
        SELECT @OrderId, N'Review', @OperatorId, ISNULL(u.UserName, N'-'),
               N'材料审阅：' + CASE @Mode WHEN N'BomOnly' THEN N'仅按整机 BOM 配料'
                                          ELSE N'确认重点零件 ' + ISNULL(p.PartName, N'#' + CAST(@PartId AS NVARCHAR(10))) END
               + CASE WHEN @BaseQty IS NOT NULL THEN N'（人工用量 ' + CAST(@BaseQty AS NVARCHAR(12)) + N'）' ELSE N'' END
               + CASE WHEN ISNULL(@Note, N'') <> N'' THEN N'；' + @Note ELSE N'' END
          FROM (SELECT 1 AS x) t
          LEFT JOIN dbo.sys_user u ON u.UserId = @OperatorId
          LEFT JOIN dbo.part p ON p.PartId = @PartId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

-- 6. 智能算料：整机 BOM × 冗余系数生成领料单（事务 + 行锁 + applock 单号 + 审阅闸门/快照）
--    语义：整机 BOM 全量配料；已确认零件属 BOM 时仅标记 ★重点，不属于 BOM 时按人工用量×冗余追加。
CREATE OR ALTER PROCEDURE dbo.usp_GeneratePicking
    @OrderId      INT,
    @Operator     NVARCHAR(30),
    @Redundancy   DECIMAL(4,2) = 1.10,     -- 冗余系数：留有一定冗余
    @NewPickingId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    BEGIN TRY
        IF (@Redundancy < 1.00 OR @Redundancy > 2.00)
            RAISERROR(N'算料失败：冗余系数应在 1.00 ~ 2.00 之间。', 16, 1);

        -- 锁定工单行：同工单并发算料串行化（另一道防线为 UQ_picking_pending_per_order）
        DECLARE @DevId INT, @RvMode NVARCHAR(20), @CPart INT, @CQty DECIMAL(10,2), @RNote NVARCHAR(200);
        SELECT @DevId = DeviceId, @RvMode = MaterialReviewMode, @CPart = ConfirmedPartId,
               @CQty = ConfirmedBaseQty, @RNote = ReviewNote
          FROM dbo.work_order WITH (UPDLOCK, HOLDLOCK)
         WHERE OrderId = @OrderId AND Status IN (N'Assigned', N'Working');
        IF @DevId IS NULL
            RAISERROR(N'算料失败：仅已派单/维修中的工单可生成领料单。', 16, 1);
        IF @RvMode IS NULL
            RAISERROR(N'算料失败：请先完成材料审阅（在工单详情确认重点零件，或选择“仅按整机 BOM 配料”）。', 16, 1);

        IF EXISTS(SELECT 1 FROM dbo.picking_list WHERE OrderId = @OrderId AND Status = N'Pending')
            RAISERROR(N'算料失败：该工单已存在未出库的领料单。', 16, 1);

        DECLARE @No NVARCHAR(30), @Seq INT, @lr INT, @Res NVARCHAR(60);
        SET @Res = N'SEQ_Picking_' + CONVERT(NVARCHAR(8), GETDATE(), 112);
        EXEC @lr = dbo.sp_getapplock
             @Resource = @Res,
             @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
        IF @lr < 0 RAISERROR(N'领料单号分配繁忙，请重试。', 16, 1);
        SELECT @Seq = ISNULL(MAX(RIGHT(PickingNo, 4)), 0) + 1
          FROM dbo.picking_list
         WHERE CONVERT(NVARCHAR(8), CreatedAt, 112) = CONVERT(NVARCHAR(8), GETDATE(), 112);
        SET @No = N'PK' + CONVERT(NVARCHAR(8), GETDATE(), 112) + RIGHT(N'000' + CAST(@Seq AS NVARCHAR(10)), 4);

        INSERT INTO dbo.picking_list(PickingNo, OrderId, GeneratedBy, Redundancy, ReviewSnapshot)
        VALUES(@No, @OrderId, @Operator, @Redundancy,
               (SELECT @RvMode AS mode, ISNULL(p.PartCode, N'') AS partCode, ISNULL(p.PartName, N'') AS partName,
                       @CQty AS baseQty, @Redundancy AS redundancy, ISNULL(@RNote, N'') AS note
                  FROM (SELECT 1 AS x) t2 LEFT JOIN dbo.part p ON p.PartId = @CPart
                 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
        SET @NewPickingId = SCOPE_IDENTITY();

        -- 整机 BOM 用量 × 冗余系数 → 向上取整 → 生成明细
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

        -- 材料审阅落料：BOM 内确认件仅标记 ★重点（数量仍按 BOM）；
        -- BOM 外补充件按人工用量×冗余追加一行，备注标明人工来源
        IF @RvMode = N'PartConfirmed'
        BEGIN
            IF EXISTS(SELECT 1 FROM dbo.picking_item WHERE PickingId = @NewPickingId AND PartId = @CPart)
                UPDATE dbo.picking_item
                   SET Remark = Remark + N'｜★重点(人工确认)'
                 WHERE PickingId = @NewPickingId AND PartId = @CPart;
            ELSE
                INSERT INTO dbo.picking_item(PickingId, PartId, PartCode, PartName, Qty, UnitPrice, Remark)
                SELECT @NewPickingId, p.PartId, p.PartCode, p.PartName,
                       CEILING(@CQty * @Redundancy), p.Price,
                       N'BOM外补充件(人工用量' + CAST(@CQty AS NVARCHAR(12)) + N'×冗余' + CAST(@Redundancy AS NVARCHAR(6)) + N')'
                FROM dbo.part p WHERE p.PartId = @CPart;
        END

        UPDATE dbo.picking_list
           SET TotalAmount = (SELECT ISNULL(SUM(Amount), 0) FROM dbo.picking_item WHERE PickingId = @NewPickingId)
         WHERE PickingId = @NewPickingId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        IF ERROR_NUMBER() IN (2601, 2627)
            RAISERROR(N'算料失败：该工单已存在未出库的领料单（并发重复生成被拦截）。', 16, 1);
        THROW;
    END CATCH
END
GO

-- 7. 领料出库：事务扣减库存 + 写流水；锁领料单与库存行，并发同单只成功一次
CREATE OR ALTER PROCEDURE dbo.usp_IssuePicking
    @PickingId INT,
    @Operator  NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    BEGIN TRY
        -- 锁定领料单（并发同单出库串行化：后到者看到非 Pending 即失败）
        DECLARE @St NVARCHAR(10);
        SELECT @St = Status FROM dbo.picking_list WITH (UPDLOCK, HOLDLOCK) WHERE PickingId = @PickingId;
        IF @St IS NULL OR @St <> N'Pending'
            RAISERROR(N'出库失败：领料单不存在或已出库。', 16, 1);

        -- 库存不足检查（锁定库存行后校验，不足回滚并指出缺口）
        IF EXISTS(SELECT 1
                  FROM dbo.picking_item i JOIN dbo.part p WITH (UPDLOCK, HOLDLOCK) ON p.PartId = i.PartId
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

        UPDATE dbo.picking_list SET Status = N'Issued', IssuedAt = GETDATE()
         WHERE PickingId = @PickingId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

-- 8. 配件入库（含流水）
CREATE OR ALTER PROCEDURE dbo.usp_StockIn
    @PartId INT,
    @Qty    INT,
    @RefNo  NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    BEGIN TRY
        IF (@Qty <= 0) RAISERROR(N'入库失败：数量必须为正。', 16, 1);
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
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

-- 9. 大屏统计：状态分布 / 近7日趋势 / 工程师排行 / 低库存
CREATE OR ALTER PROCEDURE dbo.usp_GetDashboard
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
CREATE OR ALTER VIEW dbo.vw_WorkOrder_Full
AS
SELECT o.OrderId, o.OrderNo, o.Status, o.Priority, o.FaultType, o.FaultDesc,
       o.PhotoPath, o.Recognized, o.RecognizedJson, o.CreatedAt, o.AssignTime, o.StartTime, o.FinishTime, o.RepairSummary,
       o.ReporterId, o.EngineerId,
       o.MaterialReviewMode, o.ConfirmedPartId, o.ConfirmedBaseQty, o.ReviewNote, o.ReviewedBy, o.ReviewedAt,
       cp.PartName AS ConfirmedPartName, cp.PartCode AS ConfirmedPartCode,
       rv.RealName AS ReviewedByName,
       d.DeviceCode, d.DeviceName, d.DeviceId,
       rp.RealName AS ReporterName, eg.RealName AS EngineerName
FROM dbo.work_order o
JOIN dbo.device d ON d.DeviceId = o.DeviceId
JOIN dbo.sys_user rp ON rp.UserId = o.ReporterId
LEFT JOIN dbo.sys_user eg ON eg.UserId = o.EngineerId
LEFT JOIN dbo.part cp ON cp.PartId = o.ConfirmedPartId
LEFT JOIN dbo.sys_user rv ON rv.UserId = o.ReviewedBy;
GO

CREATE OR ALTER VIEW dbo.vw_RepairReport
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

CREATE OR ALTER VIEW dbo.vw_LowStock
AS
SELECT p.PartId, p.PartCode, p.PartName, p.Spec, p.Unit, p.StockQty, p.MinStock,
       c.CategoryName, (p.MinStock - p.StockQty) AS Gap
FROM dbo.part p JOIN dbo.part_category c ON c.CategoryId = p.CategoryId
WHERE p.StockQty < p.MinStock;
GO

CREATE OR ALTER VIEW dbo.vw_PickingDetail
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
