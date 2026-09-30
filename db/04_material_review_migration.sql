/*==============================================================
  工业维修智能化零件分配平台 —— 迁移 04：材料审阅 + 写入校验
  目标结构版本：2（由迁移运行器在事务内执行，失败整体回滚）
  内容：
    1) work_order：结构化识别候选 RecognizedJson + 材料审阅字段
       （MaterialReviewMode: PartConfirmed=确认重点零件 / BomOnly=仅整机BOM；NULL=未审阅）
    2) picking_list：ReviewSnapshot 生成时审阅快照（历史清单可解释）
    3) 旧数据预检：存在负库存/非正用量即中止迁移并报告，不自动改写
    4) CHECK 约束：part 库存/下限/单价非负，bom.QtyPer 与 picking_item.Qty 为正
    5) 唯一过滤索引：一工单至多一张 Pending 领料单（并发重复生成的最后防线）
  幂等：可重复执行；本脚本不含存储过程（过程统一在 02_procs.sql 维护）
==============================================================*/

/*---------------- 1) 工单：结构化候选 + 材料审阅 ----------------*/
IF COL_LENGTH(N'dbo.work_order', N'RecognizedJson') IS NULL
    ALTER TABLE dbo.work_order ADD RecognizedJson NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.work_order', N'MaterialReviewMode') IS NULL
    ALTER TABLE dbo.work_order ADD
        MaterialReviewMode NVARCHAR(10)  NULL,   -- PartConfirmed / BomOnly
        ConfirmedPartId   INT            NULL,
        ConfirmedBaseQty  DECIMAL(10,2)  NULL,   -- BOM 外补充件的人工基础用量（BOM 内为 NULL）
        ReviewNote        NVARCHAR(200)  NULL,
        ReviewedBy        INT            NULL,
        ReviewedAt        DATETIME       NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_work_order_confirmed_part')
    ALTER TABLE dbo.work_order ADD CONSTRAINT FK_work_order_confirmed_part
        FOREIGN KEY (ConfirmedPartId) REFERENCES dbo.part(PartId);

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_work_order_reviewed_by')
    ALTER TABLE dbo.work_order ADD CONSTRAINT FK_work_order_reviewed_by
        FOREIGN KEY (ReviewedBy) REFERENCES dbo.sys_user(UserId);
GO

/*---------------- 2) 领料单：审阅快照 ----------------*/
IF COL_LENGTH(N'dbo.picking_list', N'ReviewSnapshot') IS NULL
    ALTER TABLE dbo.picking_list ADD ReviewSnapshot NVARCHAR(MAX) NULL;
GO

/*---------------- 3) 旧数据预检（违规即中止，报告样例记录） ----------------*/
DECLARE @bad NVARCHAR(300);

IF EXISTS(SELECT 1 FROM dbo.part WHERE StockQty < 0 OR MinStock < 0 OR Price < 0)
BEGIN
    SELECT TOP 1 @bad = PartCode + N'（库存' + CAST(StockQty AS NVARCHAR(12))
                         + N'/下限' + CAST(MinStock AS NVARCHAR(12))
                         + N'/单价' + CAST(Price AS NVARCHAR(12)) + N'）'
    FROM dbo.part WHERE StockQty < 0 OR MinStock < 0 OR Price < 0;
    RAISERROR(N'迁移中止：part 存在负数 库存/下限/单价，请先人工核对：%s', 16, 1, @bad);
    RETURN;
END;

IF EXISTS(SELECT 1 FROM dbo.bom WHERE QtyPer <= 0)
BEGIN
    SELECT TOP 1 @bad = N'BOM#' + CAST(BomId AS NVARCHAR(12)) + N'（用量 ' + CAST(QtyPer AS NVARCHAR(12)) + N'）'
    FROM dbo.bom WHERE QtyPer <= 0;
    RAISERROR(N'迁移中止：bom 存在非正用量，请先人工核对：%s', 16, 1, @bad);
    RETURN;
END;

IF EXISTS(SELECT 1 FROM dbo.picking_item WHERE Qty <= 0)
BEGIN
    SELECT TOP 1 @bad = PartCode + N'×' + CAST(Qty AS NVARCHAR(12))
    FROM dbo.picking_item WHERE Qty <= 0;
    RAISERROR(N'迁移中止：领料明细存在非正数量，请先人工核对：%s', 16, 1, @bad);
    RETURN;
END;
GO

/*---------------- 4) CHECK 约束（带数据校验加入） ----------------*/
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_part_stock')
    ALTER TABLE dbo.part ADD CONSTRAINT CK_part_stock CHECK (StockQty >= 0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_part_minstock')
    ALTER TABLE dbo.part ADD CONSTRAINT CK_part_minstock CHECK (MinStock >= 0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_part_price')
    ALTER TABLE dbo.part ADD CONSTRAINT CK_part_price CHECK (Price >= 0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_bom_qtyper')
    ALTER TABLE dbo.bom ADD CONSTRAINT CK_bom_qtyper CHECK (QtyPer > 0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_picking_item_qty')
    ALTER TABLE dbo.picking_item ADD CONSTRAINT CK_picking_item_qty CHECK (Qty > 0);

-- 审阅字段一致性：模式合法；PartConfirmed 必有零件；BomOnly 不绑定零件；
-- 人工基础用量（BOM 外）必须为正；BomOnly 必填理由（BOM 外理由在过程中再校验）
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_work_order_reviewmode')
    ALTER TABLE dbo.work_order ADD CONSTRAINT CK_work_order_reviewmode
        CHECK (MaterialReviewMode IS NULL OR MaterialReviewMode IN (N'PartConfirmed', N'BomOnly'));
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_work_order_reviewpart')
    ALTER TABLE dbo.work_order ADD CONSTRAINT CK_work_order_reviewpart
        CHECK (MaterialReviewMode IS NULL OR MaterialReviewMode = N'BomOnly' OR ConfirmedPartId IS NOT NULL);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_work_order_reviewqty')
    ALTER TABLE dbo.work_order ADD CONSTRAINT CK_work_order_reviewqty
        CHECK (MaterialReviewMode IS NULL OR ConfirmedBaseQty IS NULL OR ConfirmedBaseQty > 0);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = N'CK_work_order_reviewnote')
    ALTER TABLE dbo.work_order ADD CONSTRAINT CK_work_order_reviewnote
        CHECK (MaterialReviewMode IS NULL OR MaterialReviewMode = N'PartConfirmed' OR ReviewNote IS NOT NULL);
GO

/*---------------- 5) 一工单至多一张 Pending 领料单（唯一过滤索引） ----------------*/
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'UQ_picking_pending_per_order')
    CREATE UNIQUE INDEX UQ_picking_pending_per_order
        ON dbo.picking_list(OrderId) WHERE Status = N'Pending';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_picking_list_order')
    CREATE INDEX IX_picking_list_order ON dbo.picking_list(OrderId);
GO
