-- 05_performance_indexes.sql — 补充常用查询路径缺失索引
-- 幂等：IF NOT EXISTS 判断，可重跑
-- 对应结构版本 3（运行器哈希自动刷新）

-- R4：工单 SLA 颜色编码按 CreatedAt + Status 扫描 → 复合索引
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_work_order_status_created')
    CREATE INDEX IX_work_order_status_created ON dbo.work_order(Status, CreatedAt) INCLUDE(OrderId, DeviceId, EngineerId);

-- R4：配件搜索按编码/名称 → 已有 IX_part_name，补编码前缀索引
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_part_code')
    CREATE INDEX IX_part_code ON dbo.part(PartCode) INCLUDE(PartName, Spec, StockQty, MinStock);

-- R4：库存流水按配件+时间查询 → 复合索引（库存流水页面）
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_stock_log_part_time')
    CREATE INDEX IX_stock_log_part_time ON dbo.part_stock_log(PartId, CreatedAt DESC) INCLUDE(ChangeQty, AfterQty, RefNo);

-- R4：工单记录按工单+时间（时间线排序）
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_work_order_record_order_time')
    CREATE INDEX IX_work_order_record_order_time ON dbo.work_order_record(OrderId, CreatedAt) INCLUDE(Action, Operator);

-- R4：操作日志按用户+时间（审计查询）
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_op_log_user_time')
    CREATE INDEX IX_op_log_user_time ON dbo.sys_operation_log(UserId, CreatedAt DESC) INCLUDE(Module, Action, Detail);

-- R4：操作日志按模块+时间（定时任务汇总）
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_op_log_module_time')
    CREATE INDEX IX_op_log_module_time ON dbo.sys_operation_log(Module, CreatedAt DESC) INCLUDE(Action);
GO
