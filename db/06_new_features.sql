-- 06_new_features.sql — 预防性维护 + 供应商 + 停机追踪 + 配件-供应商关联
-- Schema v4；幂等可重跑

/*---------------- R41：预防性维护调度 ----------------*/
IF OBJECT_ID(N'dbo.maintenance_schedule', N'U') IS NULL
CREATE TABLE dbo.maintenance_schedule(
    ScheduleId    INT IDENTITY(1,1) PRIMARY KEY,
    DeviceId      INT           NOT NULL REFERENCES dbo.device(DeviceId),
    Title         NVARCHAR(100) NOT NULL,              -- PM 任务名（如"季度轴承润滑"）
    IntervalType  NVARCHAR(10)  NOT NULL DEFAULT N'Time',  -- Time=按天 / Meter=按运行小时
    IntervalDays  INT           NULL,                   -- Time 型：间隔天数
    IntervalHours DECIMAL(10,2) NULL,                   -- Meter 型：间隔运行小时
    LastExecutedAt DATETIME     NULL,                   -- 上次执行时间
    NextDueAt     DATETIME      NULL,                   -- 下次到期时间
    IsActive      BIT           NOT NULL DEFAULT(1),
    TemplateDesc  NVARCHAR(400) NULL,                   -- 自动生成工单的故障描述
    TemplatePriority NVARCHAR(10) NOT NULL DEFAULT N'Normal',
    CreatedAt     DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_maint_sched_due')
    CREATE INDEX IX_maint_sched_due ON dbo.maintenance_schedule(NextDueAt, IsActive) INCLUDE(DeviceId, Title);

/*---------------- R42：供应商 ----------------*/
IF OBJECT_ID(N'dbo.vendor', N'U') IS NULL
CREATE TABLE dbo.vendor(
    VendorId   INT IDENTITY(1,1) PRIMARY KEY,
    VendorName NVARCHAR(100) NOT NULL UNIQUE,
    Contact    NVARCHAR(50)  NULL,
    Phone      NVARCHAR(20)  NULL,
    Email      NVARCHAR(60)  NULL,
    Address    NVARCHAR(200) NULL,
    Status     BIT           NOT NULL DEFAULT(1),
    CreatedAt  DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO

/*---------------- R42：配件-供应商关联（含供货价与交期） ----------------*/
IF OBJECT_ID(N'dbo.part_vendor', N'U') IS NULL
CREATE TABLE dbo.part_vendor(
    PartId       INT           NOT NULL REFERENCES dbo.part(PartId),
    VendorId     INT           NOT NULL REFERENCES dbo.vendor(VendorId),
    SupplyPrice  DECIMAL(12,2) NOT NULL DEFAULT(0),   -- 供货价
    LeadTimeDays INT           NOT NULL DEFAULT(7),    -- 交期（天）
    IsPreferred  BIT           NOT NULL DEFAULT(0),    -- 首选供应商
    CONSTRAINT PK_part_vendor PRIMARY KEY(PartId, VendorId)
);
GO

/*---------------- R43：设备停机记录 ----------------*/
IF OBJECT_ID(N'dbo.downtime_record', N'U') IS NULL
CREATE TABLE dbo.downtime_record(
    DowntimeId INT IDENTITY(1,1) PRIMARY KEY,
    DeviceId   INT           NOT NULL REFERENCES dbo.device(DeviceId),
    StartTime  DATETIME      NOT NULL,
    EndTime    DATETIME      NULL,                     -- NULL = 正在停机
    Reason     NVARCHAR(200) NULL,
    CreatedBy  NVARCHAR(30)  NOT NULL,
    CreatedAt  DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_downtime_device_start')
    CREATE INDEX IX_downtime_device_start ON dbo.downtime_record(DeviceId, StartTime DESC);

/*---------------- R43：设备停机汇总视图（OEE 分母） ----------------*/
IF OBJECT_ID(N'dbo.vw_DeviceDowntime', N'V') IS NOT NULL DROP VIEW dbo.vw_DeviceDowntime;
GO
CREATE VIEW dbo.vw_DeviceDowntime
AS
SELECT d.DeviceId, d.DeviceCode, d.DeviceName,
       COUNT(dr.DowntimeId) AS DowntimeCount,
       ISNULL(SUM(CASE WHEN dr.EndTime IS NOT NULL
            THEN DATEDIFF(MINUTE, dr.StartTime, dr.EndTime) END), 0) AS TotalDownMinutes,
       ISNULL(AVG(CASE WHEN dr.EndTime IS NOT NULL
            THEN CAST(DATEDIFF(MINUTE, dr.StartTime, dr.EndTime) AS DECIMAL(10,1)) END), 0) AS AvgDownMinutes,
       SUM(CASE WHEN dr.EndTime IS NULL THEN 1 ELSE 0 END) AS ActiveDowntime
FROM dbo.device d
LEFT JOIN dbo.downtime_record dr ON dr.DeviceId = d.DeviceId
GROUP BY d.DeviceId, d.DeviceCode, d.DeviceName;
GO
