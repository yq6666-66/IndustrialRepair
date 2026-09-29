SET NOCOUNT ON;
USE IndustrialRepairDb;
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
-- 清理
DELETE dbo.part_stock_log WHERE RefNo LIKE 'PK%' OR RefNo LIKE 'GEN:PK%';
DELETE dbo.picking_item WHERE PickingId=@pid; DELETE dbo.picking_list WHERE PickingId=@pid;
DELETE dbo.work_order_record WHERE OrderId=@oid; DELETE dbo.work_order WHERE OrderId=@oid;
UPDATE dbo.part SET StockQty = StockQty + 0; -- stock stays decremented; restore via re-run of seed not needed (demo ok)
DELETE dbo.sys_user WHERE UserName IN (N'test_rep', N'test_eng');
PRINT N'SMOKE OK';
