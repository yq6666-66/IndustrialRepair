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
| 安全 | bcrypt 口令哈希、SHA-256 摘要、RBAC 四角色、图形验证码、多因素认证、登录锁定、会话超时、FluentValidation 集中校验 |
| 物联网 | SerialPort 串口 + 扫码模拟器；**MQTTnet 内嵌 Broker 遥测 + 超阈值自动报修**；**设备停机追踪**（开始/结束+汇总视图） |
| AI 辅助 | ChatGPT 等 AI 工具辅助编码（任务书允许）；拍照识别 V2 = ZXing 二维码直达 + OpenCvSharp（CLAHE/轮廓圆度）×特征库融合；**ONNX Runtime 课程模型后端**（配置就绪自动启用） |
| **预防性维护** | **maintenance_schedule 调度表（Time/Meter 双触发）→ Quartz 每 30 分钟自动检查到期 → 自动生成工单 + 通知** |
| **库存优化** | **EOQ 经济批量 √(2DS/H) + 再订购点 + 安全库存（纯 C# 算法，基于消耗历史自动计算）** |
| **供应商** | **vendor + part_vendor（供货价/交期/首选标记）** |

### 直接使用的热门开源项目（许可证以官方 NuGet 元数据为准，详见 docs/GitHub开源参考.md）

SunnyUI 3.9.8（**GPL-3.0-only**，课堂教学/课设使用；注意其传染性——发布二进制需遵循 GPL）· OpenCvSharp（Apache-2.0）· QRCoder（MIT）· ZXing.Net（Apache-2.0）· ClosedXML（MIT）· MQTTnet 4.3.7（MIT）· BCrypt.Net-Next（MIT）· xunit / Microsoft.NET.Test.Sdk（Apache-2.0）· **MailKit（Apache-2.0，真实邮件通道）· QuestPDF（Community 许可，PDF 导出）· Quartz.NET 3.13（Apache-2.0，定时任务）· Serilog + Sinks.File（Apache-2.0，日志内核）· Polly（Apache-2.0，SQL 重试）**

### SMTP 真实邮件配置（可选；不配置即演示模式）

复制 publish 目录后编辑 `appsettings.json`：

```json
{
  "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=IndustrialRepairDb;Trusted_Connection=True;TrustServerCertificate=True",
  "Smtp": {
    "Host": "smtp.qq.com",
    "Port": 465,
    "User": "你的QQ号@qq.com",
    "Password": "QQ邮箱设置→账户→开启SMTP后生成的授权码",
    "From": "你的QQ号@qq.com",
    "Ssl": true
  }
}
```

配置齐全后 MFA/找回密码验证码**真实发送到邮箱且不再界面回显**；发送失败自动回退演示并记日志。163 邮箱同理（smtp.163.com，授权码在 POP3/SMTP 设置中开启）。

## 快速开始

### 1. 环境要求

- Windows 10/11
- .NET SDK 8+（<https://dotnet.microsoft.com>）
- SQL Server LocalDB（随 Visual Studio / SSMS 安装；或 SQL Server Express）

### 1.5 本机环境清单（已配置，2026-09-30）

| 组件 | 本机状态 |
|---|---|
| Visual Studio 2022 Community 17.14（.NET 桌面开发工作负载） | ✅ 已装，可直接打开 `IndustrialRepair.sln` |
| .NET SDK（9.0.306 / 8.x）+ 运行时 7/8/9 | ✅ 已装 |
| SQL Server LocalDB（实例 MSSQLLocalDB）+ IndustrialRepairDb | ✅ 已建库播种（16 表 / 9 存储过程 / 3 触发器 / 4 视图） |
| SSMS（SQL Server Management Studio） | ✅ 已装（连 `(localdb)\MSSQLLocalDB`） |
| 7-Zip | ✅ 已装（打包提交件） |
| git 仓库 | ✅ 已初始化并首次提交（本地仓库） |
| Release 发布包 | ✅ `publish/IndustrialRepair.UI.exe`，桌面快捷方式「工业维修智能化零件分配平台」 |

> 换机重装参考：`winget install Microsoft.DotNet.SDK.8 7zip.7zip Microsoft.SQLServerManagementStudio`（VS2022 Community 用 Visual Studio Installer 勾选「.NET 桌面开发」），然后登录页点「一键初始化」。

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
├─ test/SmokeConsole/       # 无界面端到端冒烟测试（85 项断言）
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

- `dotnet build -m:1`：0 错误 0 警告（解决方案含 UI/BLL/DAL/Models、tests/IndustrialRepair.Tests、test/SmokeConsole、test/RecognizeEval）
- `dotnet test tests/IndustrialRepair.Tests`：**169/169**（BOM 算料 / 口令哈希 / 扫码协议 / 候选规整 / CSV 转义 / 评测统计 / 服务层守卫 / 口令策略 / 邮件 / PDF / 通知中心 / 文件日志 / FluentValidation 校验 / CsvHelper / ONNX 后端 / 模糊搜索 / EOQ+OEE 算法含交期回退 / 迁移脚本嵌入 / 主题色板 WCAG 对比度 20 / 大屏全屏状态机 4）
- `dotnet run --project test/SmokeConsole -- --conn="<测试库连接串>"`：**85/85**（迁移+结构校验、认证/RBAC、工单全流程含服务层越权拒绝、触发器审计、材料审阅全场景[BomOnly/快照/禁改/BOM内★/BOM外补量/留痕/快照不变性]、C#↔存储过程算料一致、并发出库恰好一次、大屏/报表/串口、QRCoder↔ZXing 回环、OpenCV、ClosedXML、MQTT 确定性越限+冷却、PM 到期自动建单+硬关联回写、EOQ 建议、停机追踪、批量删除单事务+引用保护）
  - 冒烟含写操作，必须显式 `--conn=` 指向**独立测试库**（如 `IndustrialRepair_Test_R4`），指向业务库 `IndustrialRepairDb` 时拒绝启动；`--ensure-structure` 模式可用于升级演练；`--diag-script=` 逐批定位脚本错误
- 数据库升级（M0 机制）：空库初始化、旧库升级（备份恢复演练：版本 2 + 13 工单保留）、重复启动幂等、故意失败批次整体回滚
- AI 评测工具 `test/RecognizeEval`：`--mode=qr` 扫码回环 4/4；`--mode=visual` 输出 Top1/Top3/未匹配/负样本错误接受/混淆矩阵（合成图实测 Top1 0/3——如实呈现当前特征库引擎局限，真实外观效果待独立真实图集评测）

## 当前边界与如实说明（验收口径）

- **MFA/找回密码为演示流程**：邮箱验证码在界面直接显示，未接入真实 SMTP；报告与演示按“模拟邮箱通道”表述
- **识别 Score 是匹配分数**（颜色/形状/尺寸相似度加权），不是校准后的正确概率；低分（<60）仅作人工核对提示
- **未实测项**：真实扫码枪、山海鲸实际大屏（可用大屏页“导出CSV”作数据源）、课程识别模型效果——均标注“待实物/待模型验证”，未用模拟冒充
- **整机 BOM 配料语义**（用户已确认）：算料生成整台设备 BOM；人工确认仅标记维修重点（BOM 内★标记不改数量，BOM 外按人工用量×冗余追加）；生成领料前必须完成材料审阅
- 权限双层：界面按钮显隐 + **服务层会话/权限/获派校验**（越权直接调用服务被拒绝，冒烟有断言）

## 架构约定（对照《项目级代码规范》，详见 docs/代码规范符合性说明.md）

- **版本化数据库迁移**（dbo.schema_version）：基线(01)→材料审阅(04) 逐版本事务执行，失败整体回滚可重试；02_procs 按文件 SHA-256 增量刷新（CREATE OR ALTER）；启动仅结构检查，播种只走「一键初始化」
- **写入加固**：单号 sp_getapplock 串行分配；算料/出库 UPDLOCK 行锁 + 一工单一张 Pending 单唯一过滤索引（并发只成功一次）；CHECK 约束（预检旧数据）；工单触发器只记真实新增与状态变化
- **材料审阅闭环**（M1）：识别候选 JSON 留痕 → 工程师确认重点零件或 BomOnly → 算料闸门（未审阅拒算）→ 快照随领料单固化（历史清单可解释）
- **接口隔离**：`IImageRecognizer`（课程模型同接口替换）/ `IScannerInput`（串口/模拟器/图片/手动同流程）；照片一次准备（压缩+识别）贯穿预览与提交
- **服务层守卫**：会话 + 权限 + 操作者一致 + 工程师限获派单（界面显隐之外的第二层）
- **测试隔离**：冒烟/评测工具必须显式 `--conn` 指向独立测试库，业务库拒绝写入型测试
