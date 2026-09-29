# GitHub 开源项目调研与集成说明

> 本文档对应任务书「参考相关开源项目并直接使用热门开源项目」的要求，记录调研结论、借鉴点与集成方式（2026-09 调研）。

## 一、直接引入的热门开源项目（NuGet 依赖）

| # | 开源项目 | 仓库 | 热度 | 协议 | 版本 | 本项目用途 |
|---|---|---|---|---|---|---|
| 1 | **SunnyUI** | [yhuse/SunnyUI](https://github.com/yhuse/SunnyUI) | ~4.0k★ GVP | MIT | 3.9.8 | 全套界面控件与图表（UIForm/UIButton/UIDataGridView/UIBarChart/UILineChart），登录/主框架/大屏/各业务页 |
| 2 | **OpenCvSharp** | [shimat/opencvsharp](https://github.com/shimat/opencvsharp) | ~6.1k★ | Apache-2.0 | 4.13.x | 拍照识别引擎 V2：CLAHE 自适应增强、中心 ROI 均值色、Canny 边缘 + 最大轮廓圆度（4πA/P²）判形状、轮廓面积占比判尺寸 |
| 3 | **QRCoder** | [codebude/QRCoder](https://github.com/codebude/QRCoder) | ~3.7k★ | MIT | 最新 | 配件二维码标签生成（内容 `PART:编码`，含名称文字的标签图，可另存/批量打印） |
| 4 | **ZXing.Net** | [micjahn/ZXing.Net](https://github.com/micjahn/ZXing.Net) | ~2.8k★ | Apache-2.0 | 最新 | 二维码/条码解码：报修照片含码直接命中配件（置信 100%）；串口页「从图片扫码」 |
| 5 | **ClosedXML** | [ClosedXML/ClosedXML](https://github.com/ClosedXML/ClosedXML) | ~4.6k★ | MIT | 最新 | 领料清单/维修报表/配件库存导出 .xlsx（标题、表头样式、冻结行、自适应列宽、合计行） |
| 6 | **MQTTnet** | [dotnet/MQTTnet](https://github.com/dotnet/MQTTnet) | ~4k★（.NET 基金会） | MIT | 4.3.7.1207 | IoT 网关：应用内嵌 MqttServer(127.0.0.1:18835) + 设备遥测模拟发布/订阅 + 超阈值自动报修工单 |
| 7 | **BCrypt.Net-Next** | [BcryptNet/bcrypt.net](https://github.com/BcryptNet/bcrypt.net) | ~1k★ | MIT | 4.2.0 | 口令 bcrypt 哈希（cost=11） |

**版本说明**：MQTTnet 必须锁定 **4.3.7.1207**——4.x 最终版（1208 已下架；5.x 移除了内嵌 Server 且 API 不兼容）。OpenCvSharp 需同时安装 `OpenCvSharp4` 与 `OpenCvSharp4.runtime.win`（原生库，发布体积 +约 60MB；运行异常时识别引擎自动降级纯托管规则路径）。

## 二、参考借鉴的项目（不引依赖，借鉴设计与模式）

| 项目 | 借鉴点 |
|---|---|
| [yhuse/SunnyUI 官方 Demo](https://github.com/yhuse/SunnyUI) | 多页面开发框架形态、图表数据绑定（UILineOption.AddSeries/Add）、控件属性用法（本文按 3.9.8 反射探针核实后编码） |
| [shimat/opencvsharp_samples](https://github.com/shimat/opencvsharp) | FindContours/ContourArea/ArcLength/Canny/CLAHE 的 C# 调用范式 |
| [jingz/CMMS](https://github.com/jingz/CMMS)、[github.com/topics/cmms](https://github.com/topics/cmms) | CMMS 领域工单状态机（报修→派单→维修→完工/取消）、资产台账与 BOM 字段设计、维修历史留痕思路 |
| [dotnet/MQTTnet Samples](https://github.com/dotnet/MQTTnet/blob/master/Samples) | MqttServerOptionsBuilder 端点启用（`WithDefaultEndpoint()` 必须显式调用）、订阅回调异步模式 |

## 三、集成位置索引（代码对照）

| 集成项 | BLL | UI |
|---|---|---|
| OpenCV+ZXing 识别引擎 V2 | `RecognitionEngine.cs` | `FReportDialog`（拍照识别 Top3） |
| ZXing 图片解码 | `BarcodeUtil.cs` | `UcSerial`「从图片扫码」 |
| QRCoder 标签 | `QrLabelService.cs` | `UcParts`「生成二维码」+ `FQrPreview` 预览/另存 |
| ClosedXML 导出 | `ExcelService.cs`、`PickingService.ExportExcel` | `UcParts`/`UcPicking`/`UcReport`「导出Excel」 |
| MQTTnet IoT 网关 | `MqttGateway.cs`（含 FileLog） | `UcIoT`（遥测曲线/健康表/自动报修日志） |

## 四、许可证合规

全部直接依赖为 **MIT / Apache-2.0** 宽松协议：允许闭源使用与再分发，仅需保留版权声明。发布包中随附第三方 NOTICE 即可（毕业实训场景无额外义务）。
