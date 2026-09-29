# 工业维修智能化零件分配平台

软件工程综合实训（国教软件工程 23 级 E511/E512）——「拍照 · 识别 · 智能算料」一体化智能维修系统。

维修人员报修时拍照上传 → 系统自动识别涉及的维修部件 → 结合 BOM 材料表按冗余系数智能推算所需配件与数量 → 生成领料单出库 → 全程工单流转留痕，大屏可视化统计。

## 技术栈（与任务书对应）

| 要求 | 实现 |
|---|---|
| C# WinForm | .NET 8 WinForm，三层架构（UI / BLL / DAL / Models） |
| SunnyUI | NuGet 3.9.8：UIForm / UIButton / UITextBox / UIComboBox / UIDataGridView / UIBarChart / UILineChart |
| ADO.NET | SqlConnection / SqlCommand / SqlDataAdapter / DataSet / BeginTransaction-Commit-Rollback / SqlException |
| SQL Server | LocalDB，9 张存储过程、3 个触发器、4 个视图、参数化查询防注入 |
| 安全 | bcrypt 口令哈希、SHA-256 摘要、RBAC 四角色、图形验证码、多因素认证、登录锁定、会话超时 |
| 物联网 | SerialPort 串口 + 扫码模拟器；**MQTTnet 内嵌 Broker 遥测 + 超阈值自动报修（UcIoT 页）** |
| AI 辅助 | ChatGPT 等 AI 工具辅助编码（任务书允许）；拍照识别 V2 = ZXing 二维码直达 + OpenCvSharp（CLAHE/轮廓圆度）×特征库融合 |

### 直接使用的热门开源项目（详见 docs/GitHub开源参考.md）

SunnyUI（~4k★ MIT）· OpenCvSharp（~6.1k★ Apache-2.0）· QRCoder（~3.7k★ MIT）· ZXing.Net（~2.8k★ Apache-2.0）· ClosedXML（~4.6k★ MIT）· MQTTnet 4.3.7（~4k★ .NET 基金会 MIT）· BCrypt.Net-Next（MIT）

## 快速开始

### 1. 环境要求

- Windows 10/11
- .NET SDK 8+（<https://dotnet.microsoft.com>）
- SQL Server LocalDB（随 Visual Studio / SSMS 安装；或 SQL Server Express）

### 2. 初始化数据库（二选一）

**方式 A（推荐）**：直接运行程序，登录界面点 **「一键初始化」** 按钮——自动建库、建表、存储过程/触发器/视图、播种默认账号与演示工单。

**方式 B（命令行）**：

```bash
sqllocaldb start MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -f 65001 -i db/01_init.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -f 65001 -i db/02_procs.sql
```

> 首次登录时若检测到无用户账号，程序也会自动播种默认账号。

### 3. 编译运行

```bash
dotnet build -m:1          # 本机 MSBuild 并行节点异常时务必带 -m:1
dotnet run --project src/IndustrialRepair.UI
```

Visual Studio 2022 直接打开 `IndustrialRepair.sln`，将 IndustrialRepair.UI 设为启动项目即可。

### 4. 默认账号（密码均为 123456）

| 账号 | 角色 | 权限要点 |
|---|---|---|
| admin | 系统管理员 | 全部权限：用户/角色/MFA/日志 |
| warehouse | 配件库管理员 | 配件 CRUD、入库、出库、大屏 |
| engineer | 维修工程师 | 接单、完工、算料、大屏 |
| reporter | 报修人 | 报修（拍照上传）、查询 |

## 演示路径（答辩 5 分钟脚本）

1. `reporter` 登录 → 工单中心 → 新建报修：选设备、选照片（可用配件二维码标签或任意机械件照片）→ 右侧实时显示「拍照识别 Top3」（二维码 100% 命中 / OpenCV 圆度+主色理由）→ 提交；
2. `admin` 登录 → 该工单「派单」给工程师；
3. `engineer` 登录 → 「接单」→ 智能算料·领料：选该工单，冗余系数 1.10 → 生成领料单（BOM×冗余自动明细）→「领料出库」（库存扣减、写流水、触发器预警）→「导出Excel」；
4. `engineer` → 回到工单「完工」，填写维修总结；
5. 任意账号 → 数据可视化大屏：状态分布柱图 / 近 7 日折线 / 工程师排行 / 库存预警表；
6. `admin` → 串口·扫码：配件库「生成二维码」→ 串口页「从图片扫码」（ZXing 闭环）或启动「扫码模拟器」；
7. `admin` → IoT·MQTT遥测：点「启动网关」——内嵌 Broker 收设备遥测，实时温度曲线/健康表，温度或振动越限自动创建 `[IoT自动报修]` 工单（回到工单中心可见）；
8. `admin` → 用户与日志：登录日志 / 操作日志（全程留痕）。

## 目录结构

```
IndustrialRepair/
├─ IndustrialRepair.sln
├─ db/                      # SQL 脚本（01 建库建表+种子 / 02 存储过程+触发器+视图 / 03 SQL 冒烟测试）
├─ src/
│  ├─ IndustrialRepair.UI/     # WinForm + SunnyUI 界面层（FLogin/FMain/Uc* 页面）
│  ├─ IndustrialRepair.BLL/    # 业务层（认证/工单/算料/识别/串口/会话）
│  ├─ IndustrialRepair.DAL/    # ADO.NET 数据访问层（DbHelper + 仓储）
│  └─ IndustrialRepair.Models/ # 实体
├─ test/SmokeConsole/       # 无界面端到端冒烟测试（24 项断言）
└─ docs/                    # 报告素材、截图
```

## 任务书课时对照

| 任务书课时 | 系统模块 |
|---|---|
| 系统架构设计与数据库搭建 | 三层解决方案 + db/01、02 脚本 + CRUD 基础框架（DAL DbHelper） |
| 配件库管理员信息管理 | UcParts（配件 CRUD、模糊检索、入库、预警、流水） |
| 维修工程师信息管理 | UcWorkOrders（工单创建/派单/接单/完工/取消、历史记录、报表） |
| 业务任务与系统集成 | UcPicking（算料/领料/出库/导出）+ 测试部署 |
| 安全增强与多因素认证 | FLogin（验证码/bcrypt/锁定/MFA/找回密码）+ RBAC + 会话超时 |
| 管理员界面与实战部署 | UcUsers（账号/角色/日志）+ 生产环境部署与性能调优 |
| 拍照功能/零件检测 | FReportDialog + RecognitionEngine（特征库匹配，本地压缩上传） |
| 智能算料（配料） | usp_GeneratePicking（BOM×冗余）+ 领料清单生成/导出 |
| 数据可视化大屏 | UcDashboard（UIBarChart/UILineChart/预警表/汇总卡片） |
| 物联网设备集成（串口） | UcSerial + SerialPortService（真实串口 + 模拟器） |

## 部署到其他电脑（生产环境）

1. 目标机安装 .NET Desktop Runtime 8 与 SQL Server LocalDB/Express；
2. `dotnet publish src/IndustrialRepair.UI -c Release -o publish`（db/*.sql 会随输出复制）；
3. 拷贝 publish 目录，双击 exe → 点「一键初始化」→ 完成。
   连接串可经 `appsettings.json` 覆盖：`{ "ConnectionString": "Server=...;Database=...;Trusted_Connection=True;" }`

## 已验证

- `dotnet build`：0 错误 0 警告
- `dotnet run --project test/SmokeConsole`：31/31 断言通过（认证/RBAC/工单/算料/出库/报表/大屏/串口解析/会话 + QRCoder↔ZXing 二维码回环 / OpenCV 原生库识别 / ClosedXML 导出 / MQTT 内嵌 Broker 收发）
- SQL 冒烟：`sqlcmd -i db/03_smoke_test.sql`（报修→派单→接单→算料→出库→完工→报表→自清理）
