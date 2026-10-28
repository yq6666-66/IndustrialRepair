# 工业维修智能化零件分配平台

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6)](https://learn.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

软件工程综合实训作品（国教软件工程 23 级 E511/E512）。系统面向设备维修场景，把「拍照 · 识别 · 智能算料」串成一条闭环：维修人员报修时拍照上传，系统识别照片中涉及的维修部件，结合 BOM 材料表按冗余系数推算所需配件与数量，生成领料单出库，工单全程流转留痕，最后由可视化大屏汇总统计。

工程为 .NET 8 WinForms 三层架构（UI / BLL / DAL / Models），数据库使用 SQL Server LocalDB，共 21 表、14 个存储过程、3 个触发器、8 个视图，采用版本化迁移（Schema v5）。管理员、库管员、工程师、报修人四种角色覆盖报修、派单、接单、算料、出库、完工的完整流程。

仓库地址 <https://github.com/yq6666-66/IndustrialRepair>，版本标签 v1.0.0 至 v1.8.0，许可证为 GPL-3.0（见文末说明）。

## 功能概览

| 模块 | 页面 | 主要能力 |
|---|---|---|
| 工单中心 | UcWorkOrders | 报修创建、派单、接单、完工、取消、历史记录与工单报表 |
| 智能算料与领料 | UcPicking | BOM × 冗余系数算料、材料审阅闸门、领料出库、Excel 与 PDF 导出 |
| 配件管理 | UcParts | 配件增删改查、模糊检索、入库与库存调整、库存预警、二维码标签、批量删除 |
| 数据可视化大屏 | UcDashboard | 状态分布柱图、近 7 日趋势折线、维修成本榜、工程师排行、库存预警表、30 秒后台刷新、F11 全屏 |
| 设备与维护 | UcDevices / UcMaintenance | 设备台账、维修历史、预防性维护调度、停机原因追踪与汇总 |
| 串口与扫码 | UcSerial | 真实串口接入、扫码模拟器、图片扫码、二维码生成与回环验证 |
| IoT 遥测 | UcIoT | 内嵌 MQTT Broker、实时温度与振动曲线、设备健康表、越限自动报修 |
| 用户与日志 | UcUsers | 账号与角色管理、权限配置、登录日志、操作日志、多因素认证 |
| 报表 | UcReport | 多维统计查询与导出 |
| 全局搜索 | FGlobalSearch | 跨模块关键字检索与快速跳转 |

## 技术栈（对应任务书要求）

| 要求 | 实现 |
|---|---|
| C# WinForm | .NET 8 WinForms，三层架构，解决方案含 9 个项目（4 个主工程 + 单元测试 + 冒烟 + 识别评测 + 演示数据 + ONNX 验证） |
| SunnyUI | NuGet 3.9.8：UIForm、UIButton、UITextBox、UIComboBox、UIDataGridView、UIPanel、UIBarChart、UILineChart |
| ADO.NET | SqlConnection、SqlCommand、SqlDataAdapter、DataSet、BeginTransaction-Commit-Rollback、SqlException |
| SQL Server | LocalDB，21 表、14 个存储过程、3 个触发器、8 个视图，Schema v5 版本化迁移，参数化查询防注入 |
| 安全 | bcrypt 口令哈希、SHA-256 摘要、RBAC 四角色、图形验证码、多因素认证、登录锁定、会话超时、FluentValidation 集中校验 |
| 物联网 | 串口与扫码模拟器；MQTTnet 内嵌 Broker 遥测，越限自动创建报修工单；设备停机追踪 |
| AI 辅助 | ChatGPT 等 AI 工具辅助编码（任务书允许）；识别 V2 为 ZXing 二维码直达加 OpenCvSharp 特征匹配；ONNX Runtime 课程模型后端配置即启用 |
| 预防性维护 | maintenance_schedule 调度表支持时间与计量双触发，Quartz 每 30 分钟检查到期并自动建单通知 |
| 库存优化 | EOQ 经济批量 √(2DS/H)、再订购点、安全库存，基于消耗历史与供应商实际交期计算 |
| 供应商 | vendor 与 part_vendor 记录供货价、交期与首选标记，算料优先取首选供应商交期 |

### 直接使用的开源项目

界面与图表方面，SunnyUI 3.9.8（GPL-3.0-only，见文末许可证说明）与 ScottPlot 5（MIT）负责深色主题界面与 EOQ 交互图。视觉识别方面为 OpenCvSharp4（Apache-2.0）与 Microsoft.ML.OnnxRuntime（MIT，课程模型预留后端）。条码方面为 QRCoder（MIT）与 ZXing.Net（Apache-2.0）。导出与文档方面为 ClosedXML（MIT）、QuestPDF（Community 许可）与 CsvHelper（MS-PL/Apache-2.0）。

通信与基础设施方面为 MQTTnet 4.3.7（MIT，内嵌 Broker）、MailKit（Apache-2.0，真实邮件通道）、Serilog 与 Serilog.Sinks.File（Apache-2.0，日志）、Polly（Apache-2.0，SQL 重试）、Quartz.NET 3.13（Apache-2.0，定时任务）、FluentValidation（Apache-2.0，集中校验）、BCrypt.Net-Next（MIT，口令哈希）、FuzzySharp（MIT，模糊搜索）与 Bogus（MIT，压测数据）。完整清单、集成位置与许可合规说明见 [docs/GitHub开源参考.md](docs/GitHub开源参考.md)。

## 快速开始

### 1. 环境要求

需要 Windows 10 或 11、.NET SDK 8 或更高版本（<https://dotnet.microsoft.com>），以及 SQL Server LocalDB。LocalDB 随 Visual Studio 或 SSMS 安装；若使用 SQL Server Express，只需修改连接串。

### 2. 初始化数据库

推荐方式是直接运行程序，在登录界面点「一键初始化」，程序会自动建库、建表、创建存储过程触发器视图，并播种默认账号与演示工单。若偏好命令行，可执行以下两条命令。

```bash
sqllocaldb start MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -f 65001 -i db/01_init.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -f 65001 -i db/02_procs.sql
```

首次登录时若检测不到用户账号，程序也会自动播种默认账号。

### 3. 编译与运行

```bash
dotnet build -m:1
dotnet run --project src/IndustrialRepair.UI
```

`-m:1` 用于规避本机并行 MSBuild 节点异常；在正常的开发机上可省略。Visual Studio 2022 或 2026 直接打开 `IndustrialRepair.sln`，将 IndustrialRepair.UI 设为启动项目即可 F5 运行。

### 4. 默认账号

| 账号 | 角色 | 权限要点 |
|---|---|---|
| admin | 系统管理员 | 全部权限：用户、角色、MFA、日志 |
| warehouse | 配件库管理员 | 配件增删改查、入库、出库、大屏 |
| engineer | 维修工程师 | 接单、完工、算料、大屏 |
| reporter | 报修人 | 报修（拍照上传）、查询 |

四个账号的初始密码均为 `123456`，首次登录后建议在「修改密码」中更换（口令策略要求至少 8 位且同时含字母与数字）。

### 5. 邮件通道（可选，不配置即演示模式）

复制发布目录后编辑 `appsettings.json`，填入自己的 SMTP 参数。

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

配置齐全后，多因素认证与找回密码的验证码会真实发送到邮箱且不再在界面回显；未配置或发送失败时自动回退演示模式（验证码在界面显示）并记入日志。163 邮箱同理，主机为 smtp.163.com，授权码在 POP3/SMTP 设置中开启。

## 演示路径（答辩 5 分钟脚本）

1. 以 `reporter` 登录，进入工单中心新建报修，选择设备与照片（可用配件二维码标签或任意机械件照片），右侧实时显示「拍照识别 Top3」，提交工单。
2. 切换到 `admin`，在工单中心把该工单派给工程师。
3. 切换到 `engineer` 接单，进入智能算料·领料页，选择该工单并设冗余系数 1.10，生成领料单后执行「领料出库」，观察库存扣减、流水记录与触发器预警，并导出 Excel。
4. 仍在 `engineer` 账号下回到工单执行「完工」，填写维修总结。
5. 任意账号进入数据可视化大屏，查看状态分布柱图、近 7 日折线、工程师排行与库存预警表。
6. 以 `admin` 进入串口·扫码页，先在配件库生成二维码，再「从图片扫码」完成 ZXing 闭环，或启动扫码模拟器。
7. 以 `admin` 进入 IoT·MQTT 遥测页点「启动网关」，内嵌 Broker 接收设备遥测并实时绘制曲线，温度或振动越限时自动创建 `[IoT自动报修]` 工单。
8. 最后在用户与日志页展示登录日志与操作日志，体现全程留痕。

## 目录结构

```
IndustrialRepair/
├─ IndustrialRepair.sln
├─ db/                        # SQL 脚本 01~07：建库建表、过程触发器视图、冒烟、材料审阅迁移、索引、新功能、PM 关联
├─ src/
│  ├─ IndustrialRepair.UI/    # WinForms + SunnyUI 界面层（FLogin / FMain / Uc* 页面 / UiKit 控件工厂）
│  ├─ IndustrialRepair.BLL/   # 业务层：认证、工单、算料、识别、串口、邮件、调度、MQTT、通知
│  ├─ IndustrialRepair.DAL/   # ADO.NET 数据访问层（DbHelper + 各仓储）
│  └─ IndustrialRepair.Models/# 实体与 DTO
├─ test/
│  ├─ SmokeConsole/           # 无界面端到端冒烟（85 项断言，强制独立测试库）
│  ├─ RecognizeEval/          # 识别评测（扫码回环、Top1/Top3、混淆矩阵）
│  ├─ SeedDemoData/           # 演示数据生成（500 单压测）
│  └─ OnnxTest/               # ONNX 后端连通性验证（加载、预处理、推理、标签映射）
├─ tests/IndustrialRepair.Tests/  # xUnit 单元测试（169 项）
├─ models/                    # ONNX 模型与标签文件（本机放置，不入库）
└─ docs/                      # 用户手册、架构图、表结构说明、开源参考、答辩提纲、测试记录
```

## 质量基线

| 检查项 | 命令 | 结果 | 覆盖重点 |
|---|---|---|---|
| 编译 | `dotnet build -m:1` | 0 错误 0 警告 | 解决方案内 9 个项目全部通过 |
| 单元测试 | `dotnet test tests/IndustrialRepair.Tests` | 169/169 | BOM 算料、口令哈希、扫码协议、候选规整、CSV 转义、评测统计、服务层守卫、口令策略、邮件、PDF、通知中心、文件日志、校验器、CsvHelper、ONNX 后端、模糊搜索、EOQ 与 OEE 算法、迁移脚本嵌入、主题色板 WCAG 对比度、大屏全屏状态机 |
| 端到端冒烟 | `dotnet run --project test/SmokeConsole -- --conn="<测试库连接串>"` | 85/85 | 迁移与结构校验、认证与 RBAC、工单全流程含越权拒绝、触发器审计、材料审阅全场景、C# 与存储过程算料一致性、并发出库恰好一次、大屏与报表、二维码回环、OpenCV、Excel、MQTT 确定性越限、PM 到期自动建单、EOQ 建议、停机追踪、批量删除单事务 |
| 数据库迁移 | `--ensure-structure` 与备份恢复演练 | 通过 | 空库初始化、旧库升级（版本 2 + 13 工单保留）、重复启动幂等、故意失败批次整体回滚 |
| 识别评测 | `dotnet run --project test/RecognizeEval -- --mode=qr` 等 | 扫码回环 6/6，合成图 Top1 0/3 | 扫码链路 100% 只证明回环可靠，不代表外观分类；合成图指标如实呈现特征库局限 |
| 安全扫描 | Mimosa 深度静态扫描 | 0 发现项 | 凭据、注入、路径与依赖面 |

冒烟测试包含写操作，必须显式用 `--conn=` 指向独立测试库（例如 `IndustrialRepair_Test_R4`）；指向业务库 `IndustrialRepairDb` 时会拒绝启动，避免污染演示数据。`--diag-script=` 可逐批定位脚本错误。

本机实测性能（LocalDB、500 单演示数据）供参考：生成演示数据约 3.2 秒，分页查询低于 100 毫秒，ONNX 单张推理约 50 毫秒，领料单 PDF 导出低于 200 毫秒，空库初始化低于 5 秒。

## 已知边界与如实说明

1. 识别的 Score 是颜色、形状、尺寸相似度的加权匹配分数，不是校准后的正确概率；低于 60 分时仅作为人工核对提示。
2. 合成图视觉评测结果为 Top1 0/3，真实外观图集上的效果尚待独立评测；本仓库不回避这一局限。
3. 真实扫码枪、山海鲸实际大屏与课程识别模型效果均未实测，文档一律标注「待实物验证」或「待模型验证」，未用模拟结果冒充。
4. 整机 BOM 配料语义为：算料生成整台设备 BOM，人工确认只标记维修重点（BOM 内标★不改数量，BOM 外按人工用量乘冗余追加），生成领料前必须完成材料审阅。
5. 权限为双层控制：界面按钮显隐加服务层会话、权限与获派校验，越权直接调用服务会被拒绝，冒烟测试有对应断言。
6. MFA 与找回密码在配置 SMTP 后走 MailKit 真实发送；未配置或发送失败时回退演示模式并在界面显示验证码，同时记录日志。

## 架构约定

1. 版本化数据库迁移：`dbo.schema_version` 记录已应用版本，基线（01）到材料审阅（04）逐版本按 GO 批次在同一事务内执行，失败整体回滚可重试；`02_procs.sql` 按文件 SHA-256 增量刷新，全部使用 CREATE OR ALTER；启动只做结构检查，播种仅走「一键初始化」。
2. 写入加固：单号分配用 `sp_getapplock` 串行化，算料与出库使用 UPDLOCK 行锁，一工单一张 Pending 领料单靠唯一过滤索引保证并发只成功一次，关键表有 CHECK 约束，工单触发器只记录真实新增与状态变化。
3. 材料审阅闭环：识别候选以 JSON 留痕，工程师确认重点零件或选择 BomOnly，未审阅不得算料，领料单固化审阅快照保证历史清单可解释。
4. 接口隔离：`IImageRecognizer` 可用课程模型同接口替换，`IScannerInput` 让串口、模拟器、图片与手动录入共用一条流程；照片只准备一次（压缩加识别）并贯穿预览与提交。
5. 服务层守卫：会话、权限、操作者一致性与工程师限获派单在服务层再校验一遍，构成界面显隐之外的第二道防线。
6. 测试隔离：冒烟与评测工具必须显式指定独立测试库，禁止对业务库执行写入型测试。

## 部署到其他电脑

1. 在目标机安装 .NET Desktop Runtime 8 与 SQL Server LocalDB 或 Express。
2. 执行 `dotnet publish src/IndustrialRepair.UI -c Release -o publish`，`db/*.sql` 会随输出一起复制。
3. 拷贝 publish 目录，双击 exe 后点「一键初始化」即可；连接串可通过 `appsettings.json` 覆盖，例如 `{ "ConnectionString": "Server=...;Database=...;Trusted_Connection=True;" }`。

## 文档索引

| 文档 | 内容 |
|---|---|
| [docs/用户手册.md](docs/用户手册.md) | 面向使用者的操作说明与常见问题 |
| [docs/架构图.md](docs/架构图.md) | 分层架构、模块关系与数据流 |
| [docs/数据库表结构说明.md](docs/数据库表结构说明.md) | 21 张表、视图、存储过程与触发器的字段说明（由脚本从真实库生成） |
| [docs/GitHub开源参考.md](docs/GitHub开源参考.md) | 开源库清单、集成位置索引与许可证合规 |
| [docs/代码规范符合性说明.md](docs/代码规范符合性说明.md) | 与《项目级代码规范》的逐条对照 |
| [docs/外部设备对接指南.md](docs/外部设备对接指南.md) | 扫码枪、山海鲸大屏与课程模型的接入步骤 |
| [docs/答辩提纲.md](docs/答辩提纲.md) | 演示脚本、权限矩阵与高频问答 |
| [docs/测试记录/](docs/测试记录/) | 各轮测试与验收证据存档 |

## 许可证

本项目以 [GPL-3.0](LICENSE) 发布。原因是界面层依赖 SunnyUI 3.9.8（GPL-3.0-only），分发本项目的编译产物时必须整体遵循 GPL-3.0 条款；用于课程学习、二次开发或再分发时，请同样保留本许可证与原作者版权声明。其余第三方组件为 MIT、Apache-2.0、MS-PL 与 QuestPDF Community 许可，明细见 [docs/GitHub开源参考.md](docs/GitHub开源参考.md)。
