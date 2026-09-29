/*==============================================================
  工业维修智能化零件分配平台 —— 数据库初始化脚本（可重复执行）
  环境：SQL Server 2016+ / LocalDB
  说明：建库、建表、索引、基础种子数据（角色/权限/字典/设备/配件/BOM/演示工单）
        用户账号由客户端首次启动时用 bcrypt 哈希播种（见 BLL.DbSeeder）
==============================================================*/
SET NOCOUNT ON;
IF DB_ID(N'IndustrialRepairDb') IS NULL
    CREATE DATABASE IndustrialRepairDb;
GO
USE IndustrialRepairDb;
GO

/*---------------- 1. 角色表（RBAC） ----------------*/
IF OBJECT_ID(N'dbo.sys_role', N'U') IS NULL
CREATE TABLE dbo.sys_role(
    RoleId      INT IDENTITY(1,1) PRIMARY KEY,
    RoleCode    NVARCHAR(20)  NOT NULL UNIQUE,      -- Admin/Warehouse/Engineer/Reporter
    RoleName    NVARCHAR(50)  NOT NULL,
    Description NVARCHAR(200) NULL
);
GO

/*---------------- 2. 用户表 ----------------*/
IF OBJECT_ID(N'dbo.sys_user', N'U') IS NULL
CREATE TABLE dbo.sys_user(
    UserId          INT IDENTITY(1,1) PRIMARY KEY,
    UserName        NVARCHAR(30)  NOT NULL UNIQUE,
    PasswordHash    NVARCHAR(200) NOT NULL,          -- bcrypt 哈希
    RealName        NVARCHAR(30)  NOT NULL,
    Phone           NVARCHAR(20)  NULL,
    Email           NVARCHAR(60)  NULL,
    RoleId          INT           NOT NULL REFERENCES dbo.sys_role(RoleId),
    MfaEnabled      BIT           NOT NULL DEFAULT(0),   -- 多因素认证开关
    MfaCode         NVARCHAR(10)  NULL,                  -- 模拟邮箱二次验证码
    Status          BIT           NOT NULL DEFAULT(1),   -- 1启用 0禁用
    FailCount       INT           NOT NULL DEFAULT(0),   -- 连续失败次数（锁定用）
    LockedUntil     DATETIME      NULL,
    LastLoginTime   DATETIME      NULL,
    SessionToken    UNIQUEIDENTIFIER NULL,               -- 会话令牌（超时处理）
    SessionTime     DATETIME      NULL,                  -- 令牌签发时间
    CreatedAt       DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_sys_user_role')
    CREATE INDEX IX_sys_user_role ON dbo.sys_user(RoleId);
GO

/*---------------- 3. 权限点 / 角色-权限 ----------------*/
IF OBJECT_ID(N'dbo.sys_permission', N'U') IS NULL
CREATE TABLE dbo.sys_permission(
    PermCode   NVARCHAR(40)  PRIMARY KEY,             -- 如 part.manage
    PermName   NVARCHAR(60)  NOT NULL,
    Module     NVARCHAR(30)  NOT NULL
);
GO
IF OBJECT_ID(N'dbo.sys_role_permission', N'U') IS NULL
CREATE TABLE dbo.sys_role_permission(
    RoleId   INT          NOT NULL REFERENCES dbo.sys_role(RoleId),
    PermCode NVARCHAR(40) NOT NULL REFERENCES dbo.sys_permission(PermCode),
    PRIMARY KEY(RoleId, PermCode)
);
GO

/*---------------- 4. 日志表 ----------------*/
IF OBJECT_ID(N'dbo.sys_login_log', N'U') IS NULL
CREATE TABLE dbo.sys_login_log(
    LoginLogId INT IDENTITY(1,1) PRIMARY KEY,
    UserName   NVARCHAR(30) NOT NULL,
    UserId     INT          NULL,
    Success    BIT          NOT NULL,
    Message    NVARCHAR(100) NULL,
    Ip         NVARCHAR(50) NULL,
    LoginTime  DATETIME     NOT NULL DEFAULT(GETDATE())
);
GO
IF OBJECT_ID(N'dbo.sys_operation_log', N'U') IS NULL
CREATE TABLE dbo.sys_operation_log(
    OpLogId   INT IDENTITY(1,1) PRIMARY KEY,
    UserId    INT          NOT NULL,
    UserName  NVARCHAR(30) NOT NULL,
    Module    NVARCHAR(30) NOT NULL,
    Action    NVARCHAR(60) NOT NULL,
    Detail    NVARCHAR(400) NULL,
    CreatedAt DATETIME     NOT NULL DEFAULT(GETDATE())
);
GO

/*---------------- 5. 配件类别 / 配件（含识别特征库） ----------------*/
IF OBJECT_ID(N'dbo.part_category', N'U') IS NULL
CREATE TABLE dbo.part_category(
    CategoryId   INT IDENTITY(1,1) PRIMARY KEY,
    CategoryName NVARCHAR(50) NOT NULL UNIQUE
);
GO
IF OBJECT_ID(N'dbo.part', N'U') IS NULL
CREATE TABLE dbo.part(
    PartId       INT IDENTITY(1,1) PRIMARY KEY,
    PartCode     NVARCHAR(30)  NOT NULL UNIQUE,
    PartName     NVARCHAR(60)  NOT NULL,
    CategoryId   INT           NOT NULL REFERENCES dbo.part_category(CategoryId),
    Spec         NVARCHAR(60)  NULL,                 -- 规格
    Unit         NVARCHAR(10)  NOT NULL DEFAULT N'个',
    Price        DECIMAL(12,2) NOT NULL DEFAULT(0),
    StockQty     INT           NOT NULL DEFAULT(0),
    MinStock     INT           NOT NULL DEFAULT 0,   -- 库存下限（触发器预警）
    FeatureJson  NVARCHAR(300) NULL,                 -- 拍照识别特征：{"color":[r,g,b],"shape":"circle","size":"small"}
    Status       BIT           NOT NULL DEFAULT 1,
    CreatedAt    DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_part_category')
    CREATE INDEX IX_part_category ON dbo.part(CategoryId);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_part_name')
    CREATE INDEX IX_part_name ON dbo.part(PartName);   -- 模糊检索
GO

/*---------------- 6. 设备台账 ----------------*/
IF OBJECT_ID(N'dbo.device', N'U') IS NULL
CREATE TABLE dbo.device(
    DeviceId    INT IDENTITY(1,1) PRIMARY KEY,
    DeviceCode  NVARCHAR(30)  NOT NULL UNIQUE,
    DeviceName  NVARCHAR(60)  NOT NULL,
    Location    NVARCHAR(60)  NULL,
    Status      BIT           NOT NULL DEFAULT 1
);
GO

/*---------------- 7. BOM 材料表（设备-配件用量对应） ----------------*/
IF OBJECT_ID(N'dbo.bom', N'U') IS NULL
CREATE TABLE dbo.bom(
    BomId    INT IDENTITY(1,1) PRIMARY KEY,
    DeviceId INT           NOT NULL REFERENCES dbo.device(DeviceId),
    PartId   INT           NOT NULL REFERENCES dbo.part(PartId),
    QtyPer   DECIMAL(10,2) NOT NULL DEFAULT(1),       -- 单台设备维修常规用量
    Remark   NVARCHAR(100) NULL,
    CONSTRAINT UQ_bom UNIQUE(DeviceId, PartId)
);
GO

/*---------------- 8. 工单 ----------------*/
IF OBJECT_ID(N'dbo.work_order', N'U') IS NULL
CREATE TABLE dbo.work_order(
    OrderId     INT IDENTITY(1,1) PRIMARY KEY,
    OrderNo     NVARCHAR(30)  NOT NULL UNIQUE,        -- WO20261009xxx
    DeviceId    INT           NOT NULL REFERENCES dbo.device(DeviceId),
    ReporterId  INT           NOT NULL REFERENCES dbo.sys_user(UserId),
    EngineerId  INT           NULL REFERENCES dbo.sys_user(UserId),
    FaultDesc   NVARCHAR(400) NOT NULL,
    FaultType   NVARCHAR(30)  NULL,                   -- 机械/电气/液压/其他
    Priority    NVARCHAR(10)  NOT NULL DEFAULT N'Normal',  -- Normal/Urgent/Critical/Special
    PhotoPath   NVARCHAR(260) NULL,                   -- 报修照片（本地压缩后路径）
    Recognized  NVARCHAR(400) NULL,                   -- 拍照识别结果（部件/零件 JSON 摘要）
    Status      NVARCHAR(20)  NOT NULL DEFAULT N'Pending', -- Pending/Assigned/Working/Done/Cancelled
    AssignTime  DATETIME      NULL,
    StartTime   DATETIME      NULL,
    FinishTime  DATETIME      NULL,
    RepairSummary NVARCHAR(400) NULL,
    CreatedAt   DATETIME      NOT NULL DEFAULT(GETDATE())
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_work_order_status')
    CREATE INDEX IX_work_order_status ON dbo.work_order(Status);
GO

/*---------------- 9. 工单流转记录（维修历史记录） ----------------*/
IF OBJECT_ID(N'dbo.work_order_record', N'U') IS NULL
CREATE TABLE dbo.work_order_record(
    RecordId   INT IDENTITY(1,1) PRIMARY KEY,
    OrderId    INT           NOT NULL REFERENCES dbo.work_order(OrderId),
    Action     NVARCHAR(20)  NOT NULL,                -- Create/Assign/Take/Finish/Cancel
    OperatorId INT           NULL,
    Operator   NVARCHAR(30)  NULL,
    Note       NVARCHAR(300) NULL,
    CreatedAt  DATETIME     NOT NULL DEFAULT(GETDATE())
);
GO

/*---------------- 10. 领料单 / 明细 ----------------*/
IF OBJECT_ID(N'dbo.picking_list', N'U') IS NULL
CREATE TABLE dbo.picking_list(
    PickingId   INT IDENTITY(1,1) PRIMARY KEY,
    PickingNo   NVARCHAR(30)  NOT NULL UNIQUE,        -- PK20261009xxx
    OrderId     INT           NOT NULL REFERENCES dbo.work_order(OrderId),
    GeneratedBy NVARCHAR(30)  NOT NULL,
    Redundancy  DECIMAL(4,2) NOT NULL DEFAULT 1.10,   -- 冗余系数（留有一定冗余）
    TotalAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
    Status      NVARCHAR(10)  NOT NULL DEFAULT N'Pending', -- Pending/Issued/Returned
    CreatedAt   DATETIME      NOT NULL DEFAULT(GETDATE()),
    IssuedAt    DATETIME      NULL
);
GO

IF OBJECT_ID(N'dbo.picking_item', N'U') IS NULL
CREATE TABLE dbo.picking_item(
    ItemId    INT IDENTITY(1,1) PRIMARY KEY,
    PickingId INT           NOT NULL REFERENCES dbo.picking_list(PickingId),
    PartId    INT           NOT NULL REFERENCES dbo.part(PartId),
    PartCode  NVARCHAR(30)  NOT NULL,
    PartName  NVARCHAR(60)  NOT NULL,
    Qty       INT           NOT NULL,
    UnitPrice DECIMAL(12,2) NOT NULL DEFAULT(0),
    Amount    AS (Qty * UnitPrice),                    -- 计算列
    Remark    NVARCHAR(100) NULL
);
GO

/*---------------- 11. 库存流水 / 库存预警 ----------------*/
IF OBJECT_ID(N'dbo.part_stock_log', N'U') IS NULL
CREATE TABLE dbo.part_stock_log(
    StockLogId INT IDENTITY(1,1) PRIMARY KEY,
    PartId     INT          NOT NULL REFERENCES dbo.part(PartId),
    ChangeQty  INT          NOT NULL,                  -- 正入库 负出库
    AfterQty   INT          NOT NULL,
    RefNo      NVARCHAR(30) NULL,                      -- 关联领料单号/入库单号
    CreatedAt  DATETIME    NOT NULL DEFAULT(GETDATE())
);
GO
IF OBJECT_ID(N'dbo.stock_alert', N'U') IS NULL
CREATE TABLE dbo.stock_alert(
    AlertId   INT IDENTITY(1,1) PRIMARY KEY,
    PartId    INT     NOT NULL REFERENCES dbo.part(PartId),
    StockQty  INT     NOT NULL,
    MinStock  INT     NOT NULL,
    Handled   BIT     NOT NULL DEFAULT 0,
    AlertTime DATETIME NOT NULL DEFAULT(GETDATE())
);
GO

/*==============================================================
  种子数据（均带存在性判断，可重复执行）
==============================================================*/
IF NOT EXISTS(SELECT 1 FROM dbo.sys_role)
BEGIN
INSERT INTO dbo.sys_role(RoleCode, RoleName, Description) VALUES
(N'Admin',     N'系统管理员',   N'用户/角色/权限/日志/参数管理'),
(N'Warehouse', N'配件库管理员', N'配件入库、库存、发料、领料管理'),
(N'Engineer',  N'维修工程师',   N'接单维修、完工反馈、历史记录查询'),
(N'Reporter',  N'报修人',       N'设备报修、拍照上传、进度查询');
END
GO

IF NOT EXISTS(SELECT 1 FROM dbo.sys_permission)
BEGIN
INSERT INTO dbo.sys_permission(PermCode, PermName, Module) VALUES
(N'part.manage',    N'配件信息维护(增删改查)',   N'配件库'),
(N'part.stockin',   N'配件入库',                 N'配件库'),
(N'order.report',   N'创建报修工单',             N'工单'),
(N'order.assign',   N'工单派单',                 N'工单'),
(N'order.handle',   N'工单维修处理',             N'工单'),
(N'order.view',     N'工单查询',                 N'工单'),
(N'picking.generate',N'智能算料生成领料单',      N'领料'),
(N'picking.issue',  N'领料出库',                 N'领料'),
(N'dashboard.view', N'数据可视化大屏',           N'大屏'),
(N'serial.port',    N'串口设备/扫码',            N'物联网'),
(N'user.manage',    N'用户与角色管理',           N'系统'),
(N'log.view',       N'日志查询',                 N'系统');
END
GO

IF NOT EXISTS(SELECT 1 FROM dbo.sys_role_permission)
BEGIN
INSERT INTO dbo.sys_role_permission(RoleId, PermCode)
SELECT r.RoleId, p.PermCode
FROM dbo.sys_role r
JOIN dbo.sys_permission p ON
    (r.RoleCode = N'Admin') OR
    (r.RoleCode = N'Warehouse' AND p.PermCode IN (N'part.manage',N'part.stockin',N'order.view',N'picking.generate',N'picking.issue',N'dashboard.view',N'serial.port')) OR
    (r.RoleCode = N'Engineer'  AND p.PermCode IN (N'order.handle',N'order.view',N'picking.generate',N'dashboard.view',N'serial.port')) OR
    (r.RoleCode = N'Reporter'  AND p.PermCode IN (N'order.report',N'order.view',N'dashboard.view'));
END
GO

IF NOT EXISTS(SELECT 1 FROM dbo.part_category)
BEGIN
INSERT INTO dbo.part_category(CategoryName) VALUES
(N'轴承类'),(N'密封件'),(N'电气元件'),(N'传动件'),(N'紧固件'),(N'润滑耗材');
END
GO

IF NOT EXISTS(SELECT 1 FROM dbo.part)
BEGIN
INSERT INTO dbo.part(PartCode, PartName, CategoryId, Spec, Unit, Price, StockQty, MinStock, FeatureJson, Status)
VALUES
(N'P-ZC-001', N'深沟球轴承6205', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'轴承类'),   N'6205-2RS 25x52x15', N'个', 18.50, 120, 30, N'{"color":[120,120,125],"shape":"circle","size":"small"}', 1),
(N'P-ZC-002', N'圆锥滚子轴承30308', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'轴承类'), N'30308 40x90x25',  N'个', 46.00, 60,  20, N'{"color":[130,125,120],"shape":"circle","size":"medium"}', 1),
(N'P-ZC-003', N'推力球轴承51108', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'轴承类'),   N'51108 40x60x13',  N'个', 32.00, 40,  15, N'{"color":[125,128,130],"shape":"circle","size":"small"}', 1),
(N'P-MF-001', N'骨架油封TC40x62x8', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'密封件'), N'TC 40x62x8',     N'个', 8.80,  200, 50, N'{"color":[60,60,65],"shape":"circle","size":"small"}', 1),
(N'P-MF-002', N'O型圈组合垫', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'密封件'),       N'DN20 丁腈橡胶',   N'套', 2.50,  500, 100,N'{"color":[30,30,35],"shape":"circle","size":"small"}', 1),
(N'P-MF-003', N'活塞密封修理包', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'密封件'),    N'液压缸80缸径',    N'套', 128.00,25,  10, N'{"color":[45,45,50],"shape":"rect","size":"medium"}', 1),
(N'P-DQ-001', N'三相交流接触器CJX2-0910', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'电气元件'), N'AC220V 9A', N'个', 36.00, 45, 15, N'{"color":[210,215,220],"shape":"rect","size":"small"}', 1),
(N'P-DQ-002', N'热继电器JR36-20', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'电气元件'), N'整定7-10A',    N'个', 42.00, 38, 12, N'{"color":[225,225,230],"shape":"rect","size":"small"}', 1),
(N'P-DQ-003', N'变频器散热风扇24V', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'电气元件'), N'24VDC 0.15A',  N'个', 65.00, 22,  8,  N'{"color":[70,70,75],"shape":"circle","size":"small"}', 1),
(N'P-CD-001', N'三角带B-2240', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'传动件'),      N'B型 Ld=2240',    N'条', 24.00, 80,  20, N'{"color":[40,40,42],"shape":"rect","size":"medium"}', 1),
(N'P-CD-002', N'滚子链08B-1x64', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'传动件'),    N'08B 64节',       N'条', 39.00, 30,  10, N'{"color":[160,160,165],"shape":"rect","size":"medium"}', 1),
(N'P-CD-003', N'联轴器弹性块', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'传动件'),     N'MT4 橡胶',       N'个', 15.00, 90,  25, N'{"color":[240,200,80],"shape":"rect","size":"small"}', 1),
(N'P-JG-001', N'高强度内六角螺栓M12x40', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'紧固件'), N'12.9级 镀锌', N'个', 3.60, 800, 200,N'{"color":[180,182,186],"shape":"hex","size":"small"}', 1),
(N'P-JG-002', N'膨胀螺栓M10x80', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'紧固件'),   N'不锈钢',         N'个', 2.80, 600, 150,N'{"color":[200,200,205],"shape":"hex","size":"small"}', 1),
(N'P-RH-001', N'锂基润滑脂2kg', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'润滑耗材'),  N'2# 锂基脂',      N'桶', 42.00, 60,  15, N'{"color":[250,250,245],"shape":"rect","size":"medium"}', 1),
(N'P-RH-002', N'液压油L-HM46(18L)', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'润滑耗材'), N'18L 桶装',    N'桶', 268.00,18,  6,  N'{"color":[225,180,60],"shape":"rect","size":"large"}', 1),
(N'P-RH-003', N'齿轮油SAE85W-140(4L)', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'润滑耗材'), N'4L 桶装',   N'桶', 96.00, 26,  8,  N'{"color":[200,160,50],"shape":"rect","size":"medium"}', 1),
(N'P-QT-001', N'电机碳刷DG-12', (SELECT CategoryId FROM dbo.part_category WHERE CategoryName=N'电气元件'),   N'DG-12 12.5x25',  N'对', 12.00, 70,  20, N'{"color":[35,35,38],"shape":"rect","size":"small"}', 1);
END
GO

IF NOT EXISTS(SELECT 1 FROM dbo.device)
BEGIN
INSERT INTO dbo.device(DeviceCode, DeviceName, Location) VALUES
(N'DEV-001', N'数控车床CK6140',      N'一车间-A区'),
(N'DEV-002', N'立式加工中心VMC850',  N'一车间-B区'),
(N'DEV-003', N'空气压缩机SA-37A',    N'动力站房'),
(N'DEV-004', N'桥式起重机QD-10t',    N'二车间跨中'),
(N'DEV-005', N'液压站YZ-63',         N'二车间-C区'),
(N'DEV-006', N'皮带输送机B800-30m',  N'装配线1'),
(N'DEV-007', N'冷却塔LBCM-80',       N'屋顶设备层'),
(N'DEV-008', N'注塑机HT-160T',       N'三车间');
END
GO

IF NOT EXISTS(SELECT 1 FROM dbo.bom)
BEGIN
INSERT INTO dbo.bom(DeviceId, PartId, QtyPer, Remark)
SELECT d.DeviceId, p.PartId, v.QtyPer, v.Remark
FROM (VALUES
 (N'DEV-001', N'P-ZC-001', 2,   N'主轴轴承'),
 (N'DEV-001', N'P-MF-001', 2,   N'主轴油封'),
 (N'DEV-001', N'P-JG-001', 8,   N'端盖紧固'),
 (N'DEV-001', N'P-RH-001', 1,   N'轴承润滑'),
 (N'DEV-002', N'P-DQ-001', 1,   N'主接触器'),
 (N'DEV-002', N'P-DQ-002', 1,   N'过载保护'),
 (N'DEV-002', N'P-DQ-003', 1,   N'电柜散热'),
 (N'DEV-002', N'P-QT-001',  2,   N'主轴电机碳刷'),
 (N'DEV-003', N'P-MF-002', 4,   N'管路密封'),
 (N'DEV-003', N'P-CD-001', 2,   N'传动皮带'),
 (N'DEV-003', N'P-RH-002', 1,   N'压缩机油'),
 (N'DEV-004', N'P-JG-002', 10,  N'轨道压板'),
 (N'DEV-004', N'P-ZC-002', 4,   N'车轮轴承'),
 (N'DEV-004', N'P-RH-001', 1,   N'钢丝绳润滑'),
 (N'DEV-005', N'P-MF-003', 1,   N'活塞密封'),
 (N'DEV-005', N'P-RH-002', 1,   N'液压油补充'),
 (N'DEV-005', N'P-DQ-001', 1,   N'油泵接触器'),
 (N'DEV-006', N'P-CD-001', 3,   N'输送带'),
 (N'DEV-006', N'P-ZC-001', 4,   N'托辊轴承'),
 (N'DEV-006', N'P-CD-002', 2,   N'链条'),
 (N'DEV-007', N'P-MF-002', 6,   N'管路密封'),
 (N'DEV-007', N'P-RH-003', 1,   N'齿轮油'),
 (N'DEV-008', N'P-ZC-003', 2,   N'合模机构轴承'),
 (N'DEV-008', N'P-DQ-002', 1,   N'加热回路过载'),
 (N'DEV-008', N'P-CD-003', 2,   N'预塑联轴器')
) v(DeviceCode, PartCode, QtyPer, Remark)
JOIN dbo.device d ON d.DeviceCode = v.DeviceCode
JOIN dbo.part   p ON p.PartCode  = v.PartCode;
END
GO

PRINT N'01_init.sql 执行完成：库表结构与基础数据就绪。';
GO
