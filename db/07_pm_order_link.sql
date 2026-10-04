-- =====================================================================
-- 07：PM 调度与工单硬关联（WR4 / FR4 数据库部分）
-- work_order 增加 ScheduleId 可空外键，PM 自动建单时回写，
-- 替代此前仅靠描述文本「调度编号 #N」的软标注。
-- 迁移系统按 GO 批次在单一事务内执行，失败整体回滚。
-- =====================================================================

IF COL_LENGTH(N'dbo.work_order', N'ScheduleId') IS NULL
BEGIN
    ALTER TABLE dbo.work_order ADD ScheduleId INT NULL
        REFERENCES dbo.maintenance_schedule(ScheduleId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_work_order_ScheduleId')
BEGIN
    CREATE INDEX IX_work_order_ScheduleId ON dbo.work_order(ScheduleId)
        WHERE ScheduleId IS NOT NULL;
END
GO
