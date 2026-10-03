/*==============================================================
  SQL 冒烟脚本（库中立版）
  ⚠ 只允许在独立测试库执行：sqlcmd -S "(localdb)\MSSQLLocalDB" -d <测试库名> -i db/03_smoke_test.sql -b
  本脚本不含 USE 语句（防止从测试库连接重定向到业务库）；
  业务库 IndustrialRepairDb 在首批被显式拒绝。
  依赖：结构版本 ≥ 2（usp_ReviewMaterials 审阅闸门），全程自清理。
==============================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- 含过滤索引/计算列表的 DML 与过程创建要求
GO

IF DB_NAME() = N'IndustrialRepairDb'
BEGIN
    RAISERROR(N'SQL 冒烟禁止在业务库 IndustrialRepairDb 上运行：请用 sqlcmd -d 指定独立测试库（如 IndustrialRepair_Test_R4）。', 16, 127);
    SET NOEXEC ON;
END
GO

-- 临时测试用户
INSERT INTO dbo.sys_user(UserName, PasswordHash, RealName, RoleId)
SELECT N'test_rep', N'x', N'测试报修人', RoleId FROM sys_role WHERE RoleCode=N'Reporter'
UNION ALL
SELECT N'test_eng', N'x', N'测试工程师', RoleId FROM sys_role WHERE RoleCode=N'Engineer';
DECLARE @rep INT, @eng INT;
SELECT @rep = UserId FROM sys_user WHERE UserName=N'test_rep';
SELECT @eng = UserId FROM sys_user WHERE UserName=N'test_eng';
DECLARE @oid INT, @pid INT;
EXEC dbo.usp_CreateOrder @DeviceId=1, @ReporterId=@rep, @FaultDesc=N'冒烟测试：主轴异响', @FaultType=N'机械', @Priority=N'Normal', @PhotoPath=NULL, @Recognized=NULL, @NewOrderId=@oid OUTPUT;
PRINT N'创建工单#' + CAST(@oid AS NVARCHAR(10));
EXEC dbo.usp_AssignOrder @OrderId=@oid, @EngineerId=@eng, @OperatorId=@eng;
EXEC dbo.usp_TakeOrder @OrderId=@oid, @EngineerId=@eng;
-- M1 审阅闸门：算料前必须完成材料审阅
EXEC dbo.usp_ReviewMaterials @OrderId=@oid, @Mode=N'BomOnly', @Note=N'03冒烟：整机BOM', @OperatorId=@eng;
EXEC dbo.usp_GeneratePicking @OrderId=@oid, @Operator=N'test_eng', @Redundancy=1.10, @NewPickingId=@pid OUTPUT;
PRINT N'领料单#' + CAST(@pid AS NVARCHAR(10));
SELECT COUNT(*) AS ItemCount FROM picking_item WHERE PickingId=@pid;
SELECT PartName, Qty, Amount FROM dbo.picking_item WHERE PickingId=@pid;
EXEC dbo.usp_IssuePicking @PickingId=@pid, @Operator=N'test_eng';
PRINT N'出库后库存:';
SELECT PartCode, StockQty FROM dbo.part WHERE PartId IN (SELECT PartId FROM picking_item WHERE PickingId=@pid);
EXEC dbo.usp_CompleteOrder @OrderId=@oid, @OperatorId=@eng, @Summary=N'更换轴承与油封，试机正常';
PRINT N'流转记录:';
SELECT Action, Operator, CreatedAt FROM dbo.work_order_record WHERE OrderId=@oid;
PRINT N'报表视图:';
SELECT * FROM dbo.vw_RepairReport;
-- 清理（先子后父；把出库扣减的库存补回，保持测试库可重复执行）
UPDATE p SET p.StockQty = p.StockQty + i.Qty
FROM dbo.part p JOIN dbo.picking_item i ON i.PartId = p.PartId
WHERE i.PickingId = @pid;
DELETE dbo.part_stock_log WHERE RefNo IN (SELECT PickingNo FROM dbo.picking_list WHERE PickingId=@pid)
   OR RefNo LIKE 'GEN:PK%';
DELETE dbo.picking_item WHERE PickingId=@pid;
DELETE dbo.picking_list WHERE PickingId=@pid;
DELETE dbo.work_order_record WHERE OrderId=@oid;
DELETE dbo.work_order WHERE OrderId=@oid;
DELETE dbo.sys_user WHERE UserName IN (N'test_rep', N'test_eng');
PRINT N'SMOKE OK';
SET NOEXEC OFF;   -- 恢复会话执行状态（配合文件头业务库防护）
GO
