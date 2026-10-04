# -*- coding: utf-8 -*-
"""答辩提纲 → PPT 初稿（docs/答辩_工业维修智能化零件分配平台.pptx）
用法：python docs/export_ppt.py　（生成后可在 PowerPoint 中微调）
"""
from pathlib import Path

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.util import Pt, Cm

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "答辩_工业维修智能化零件分配平台.pptx"

PRIMARY = RGBColor(0x2A, 0x6C, 0xCA)
DARK = RGBColor(0x20, 0x29, 0x38)
GRAY = RGBColor(0x6E, 0x7A, 0x8A)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
LIGHT = RGBColor(0xF2, 0xF6, 0xFA)


def add_slide(prs, layout=6):
    return prs.slides.add_slide(prs.slide_layouts[layout])


def bg(slide, color):
    slide.background.fill.solid()
    slide.background.fill.fore_color.rgb = color


def box(slide, x, y, w, h, fill=None, line=None):
    from pptx.enum.shapes import MSO_SHAPE
    sh = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Cm(x), Cm(y), Cm(w), Cm(h))
    if fill is None:
        sh.fill.background()
    else:
        sh.fill.solid(); sh.fill.fore_color.rgb = fill
    if line is None:
        sh.line.fill.background()
    else:
        sh.line.color.rgb = line
    sh.shadow.inherit = False
    return sh


def text(slide, x, y, w, h, runs, size=14, color=DARK, bold=False, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, line_spacing=1.15):
    tb = slide.shapes.add_textbox(Cm(x), Cm(y), Cm(w), Cm(h))
    tf = tb.text_frame
    tf.word_wrap = True
    tf.vertical_anchor = anchor
    items = runs if isinstance(runs, list) else [runs]
    for idx, item in enumerate(items):
        p = tf.paragraphs[0] if idx == 0 else tf.add_paragraph()
        p.alignment = align
        p.line_spacing = line_spacing
        segs = item if isinstance(item, list) else [(item, size, color, bold)]
        for seg in segs:
            t, s, c, b = (seg + (size, color, bold))[:4] if isinstance(seg, tuple) else (seg, size, color, bold)
            r = p.add_run(); r.text = t
            r.font.size = Pt(s); r.font.color.rgb = c; r.font.bold = b
            r.font.name = "微软雅黑"
    return tb


def header(slide, title, sub=""):
    box(slide, 0, 0, 33.9, 2.1, fill=PRIMARY)
    box(slide, 0.6, 0.55, 0.12, 1.0, fill=WHITE)
    text(slide, 0.95, 0.35, 24, 1.5, title, size=20, color=WHITE, bold=True, anchor=MSO_ANCHOR.MIDDLE)
    if sub:
        text(slide, 0.95, 1.55, 30, 0.6, sub, size=10, color=RGBColor(0xC5, 0xDD, 0xFF))


def bullets(slide, x, y, w, items, size=14, gap=0.95):
    for i, it in enumerate(items):
        box(slide, x, y + i * gap + 0.12, 0.16, 0.42, fill=PRIMARY)
        text(slide, x + 0.42, y + i * gap, w - 0.5, gap, it, size=size)


def main():
    prs = Presentation()
    prs.slide_width, prs.slide_height = Cm(33.9), Cm(19.05)   # 16:9

    # 1 封面
    s = add_slide(prs); bg(s, DARK)
    box(s, 0, 15.6, 33.9, 3.45, fill=PRIMARY)
    text(s, 2.5, 5.2, 29, 3, "工业维修智能化零件分配平台", size=40, color=WHITE, bold=True, align=PP_ALIGN.CENTER)
    text(s, 2.5, 8.6, 29, 1.2, "拍照 · 识别 · 审阅 · 算料 一体化智能维修系统", size=18, color=RGBColor(0x9E, 0xC5, 0xFF), align=PP_ALIGN.CENTER)
    text(s, 2.5, 16.3, 29, 1, [[("《软件工程综合实训》答辩　｜　软件工程 23 级　｜　姓名：____　学号：____", 14, WHITE, False)]], align=PP_ALIGN.CENTER)

    # 2 业务闭环
    s = add_slide(prs); header(s, "业务闭环：识别为候选，人工来确认")
    flow = ["报修拍照", "识别 Top3 候选留痕", "工程师材料审阅", "整机BOM×冗余算料", "领料出库(事务)", "完工·大屏统计"]
    x = 0.9
    for i, step in enumerate(flow):
        box(s, x, 3.2, 4.7, 2.4, fill=LIGHT, line=PRIMARY)
        text(s, x + 0.2, 3.6, 4.3, 1.8, [[(f"{i+1}", 22, PRIMARY, True)], [(step, 12.5, DARK, True)]], align=PP_ALIGN.CENTER)
        x += 5.3
    bullets(s, 1.2, 7.6, 31.5, [
        "照片判不准内部损坏：识别只给候选（匹配分数+可解释理由），<60 分标注需人工核对",
        "材料审阅：BOM 内确认仅 ★ 标记重点（数量按 BOM）；BOM 外须人工用量+理由，按 CEILING(用量×冗余) 追加",
        "存在未出库领料单禁改审阅；审阅快照随单固化——历史清单始终可解释",
    ], size=14, gap=1.5)
    text(s, 1.2, 13.6, 31, 1, "未审阅的工单一律拒绝算料（存储过程闸门）", size=13, color=GRAY)

    # 3 技术栈与架构
    s = add_slide(prs); header(s, "技术栈与三层架构", ".NET 8 WinForm + SunnyUI + ADO.NET + SQL Server LocalDB")
    layers = [("UI 层", "WinForm + SunnyUI 3.9.8（UIForm/UIDataGridView/UIBarChart/UILineChart）· 维护计划中心四Tab · 异步照片准备"),
              ("BLL 层", "认证(bcrypt/MFA) 工单 材料审阅 算料 识别 串口 MQTT · PM调度(Quartz) EOQ优化 停机OEE 供应商"),
              ("DAL 层", "ADO.NET DbHelper：SqlParameter 全参数化 · RunInTransaction · ScriptRunner 版本化迁移(Schema v5)"),
              ("Models", "实体与代码（MaterialReviewMode / InventoryRecommendation / Result 模式）")]
    y = 3.3
    for name, desc in layers:
        box(s, 1.2, y, 5.2, 2.2, fill=PRIMARY)
        text(s, 1.4, y + 0.55, 4.8, 1.2, name, size=17, color=WHITE, bold=True, align=PP_ALIGN.CENTER)
        box(s, 6.6, y, 26.1, 2.2, fill=LIGHT)
        text(s, 7.0, y + 0.35, 25.4, 1.6, desc, size=12.5)
        y += 2.9
    text(s, 1.2, y + 0.1, 31, 1, "权限双层：界面按钮显隐 + 服务层会话/权限/获派二次校验（越权调用直接拒绝）", size=13, color=GRAY)

    # 4 数据库设计
    s = add_slide(prs); header(s, "数据库：21 表 · 14 过程 · 3 触发器 · 8 视图", "版本化迁移（Schema v5）+ 写入约束")
    bullets(s, 1.2, 3.4, 15.5, [
        "usp_ReviewMaterials：审阅与留痕同事务",
        "usp_GeneratePicking：审阅闸门/快照/★标记/applock 单号",
        "usp_IssuePicking：行锁出库，不足回滚报缺口",
        "触发器只记真实新增与状态变化",
        "CHECK 约束：库存/下限/单价非负、用量为正",
        "一工单至多一张未出库单：唯一过滤索引兜底并发",
    ], size=13.5, gap=1.05)
    box(s, 17.6, 3.4, 15.2, 10.4, fill=LIGHT)
    text(s, 18.2, 3.8, 14, 1, "schema_version 版本化迁移", size=15, bold=True, color=PRIMARY)
    text(s, 18.2, 5.0, 14, 8, [
        "基线 v1 → 材料审阅 v2 → 性能索引 v3 → 新功能 v4 → PM 关联 v5",
        "任一批次失败整体回滚，版本号不落库",
        "重启自动重试；结构校验通过才提交",
        "存储过程/触发器/视图按脚本 SHA-256 增量刷新",
        "",
        "验收：空库初始化 / 旧库增量升级（v4→v5 实测）/",
        "重复启动幂等 / 坏批次整体回滚 全部通过",
    ], size=12)

    # 5 安全
    s = add_slide(prs); header(s, "安全实现")
    cards = [("口令安全", "bcrypt cost=11 随机盐\n失败5次锁10分钟\n注册/登录/找回全链路"), ("登录防护", "图形验证码(抗旋转噪线)\nMFA 邮箱二次验证(演示通道)\n30 分钟滑动会话超时"),
             ("注入防护", "SqlParameter 全参数化\n存储过程内部同样参数化\n无字符串拼接"), ("权限与审计", "RBAC 四角色 12 权限点\n服务层二次校验+获派限制\n登录/操作/审阅全留痕")]
    x = 1.2
    for t, d in cards:
        box(s, x, 3.6, 7.6, 8.6, fill=LIGHT, line=PRIMARY)
        text(s, x + 0.4, 4.1, 6.8, 1.2, t, size=16, color=PRIMARY, bold=True)
        text(s, x + 0.4, 5.8, 6.8, 6, d.split("\n"), size=12.5, line_spacing=1.5)
        x += 8.0

    # 6 测试与评测
    s = add_slide(prs); header(s, "测试与评测：出真数", "独立测试库强制隔离，不污染业务库")
    stats = [("单元测试", "138/138"), ("端到端冒烟", "82/82"), ("扫码回环评测", "6/6"), ("SQL 冒烟/升级演练", "SMOKE OK")]
    x = 1.2
    for t, v in stats:
        box(s, x, 3.4, 7.6, 3.6, fill=PRIMARY)
        text(s, x + 0.3, 3.7, 7, 1, t, size=13, color=RGBColor(0xC5, 0xDD, 0xFF), align=PP_ALIGN.CENTER)
        text(s, x + 0.3, 4.6, 7, 2, v, size=26, color=WHITE, bold=True, align=PP_ALIGN.CENTER)
        x += 8.0
    bullets(s, 1.2, 8.0, 31.5, [
        "冒烟覆盖：越权拒绝 / 触发器审计 / 审阅九场景 / C#↔存储过程算料对拍 / 并发出库恰好一次 / 快照不变性",
        "识别评测工具 RecognizeEval：visual 模式禁照片内条码作答，输出 Top1/Top3/混淆矩阵/run.json（特征库哈希+降级计数）",
        "诚实数字：合成图外观 Top1 0/3 —— 特征库匹配的真实水平；真实效果待课程模型+真实图集，不用模拟冒充",
    ], size=13.5, gap=1.5)

    # 7 开源集成与合规
    s = add_slide(prs); header(s, "开源集成与合规（17 个直接依赖）")
    rows = [("SunnyUI", "界面/图表", "GPL-3.0-only"), ("OpenCvSharp", "视觉特征", "Apache-2.0"),
            ("Microsoft.ML.OnnxRuntime", "课程模型后端", "MIT"), ("ZXing.Net", "条码解码", "Apache-2.0"),
            ("ClosedXML", "Excel 报表", "MIT"), ("MQTTnet 4.3.7", "内嵌 Broker", "MIT"),
            ("Quartz.NET 3.13", "定时任务/PM调度", "Apache-2.0"), ("ScottPlot 5", "EOQ 交互图表", "MIT")]
    for i, (n, u, l) in enumerate(rows):
        cx = 1.2 + (i % 2) * 15.9
        cy = 3.4 + (i // 2) * 2.5
        box(s, cx, cy, 15.2, 2.1, fill=LIGHT)
        text(s, cx + 0.4, cy + 0.28, 6.5, 1.5, n, size=14.5, bold=True)
        text(s, cx + 7.0, cy + 0.34, 4.6, 1.5, u, size=12, color=GRAY)
        text(s, cx + 11.2, cy + 0.28, 3.8, 1.5, l, size=12, color=PRIMARY if "GPL" in l else GRAY, bold="GPL" in l)
    text(s, 1.2, 13.9, 31, 1, "另含 MailKit/QuestPDF/CsvHelper/Serilog/Polly/FluentValidation/FuzzySharp/Bogus/QRCoder/BCrypt.Net-Next；许可以官方 NuGet 元数据为准，SunnyUI 为 GPL-3.0-only（课设可用，商用需注意传染性）", size=11.5, color=GRAY)

    # 8 集成亮点
    s = add_slide(prs); header(s, "维护智能化与集成亮点")
    bullets(s, 1.2, 3.6, 31.5, [
        "预防性维护 PM：Time/Meter 双模式调度，Quartz 每 30 分钟检查到期自动建工单，工单↔调度硬外键关联（Schema v5）",
        "EOQ 库存优化：经济批量/再订购点/安全库存，首选供应商实际交期参与计算（无关联回退 14 天），ScottPlot 图表导出 PNG",
        "停机追踪 OEE：固定七类停机原因下拉，30 天可用率（Availability）计算与 Excel 导出",
        "MQTT 遥测：内嵌 Broker(127.0.0.1:18835)，温度/振动越限自动建 [IoT自动报修] 工单（10 分钟冷却，冒烟确定性断言）",
        "二维码闭环：QRCoder 生成 PART:编码 标签 → ZXing 解码直达配件（回环 6/6）；CSV/Excel 导出对接外部大屏",
    ], size=14, gap=1.9)

    # 9 边界与展望
    s = add_slide(prs); header(s, "如实边界与展望")
    box(s, 1.2, 3.4, 15.2, 9.6, fill=LIGHT)
    text(s, 1.8, 3.8, 14, 1, "当前如实标注", size=15, bold=True, color=PRIMARY)
    text(s, 1.8, 5.1, 14, 7.5, [
        "识别分数=匹配分，非概率（合成图 Top1 0/3 已入档）",
        "MFA 邮箱为演示通道（验证码界面直显）",
        "真实扫码枪 / 山海鲸 / 课程模型：待实物验证",
    ], size=13, line_spacing=1.6)
    box(s, 17.6, 3.4, 15.2, 9.6, fill=LIGHT)
    text(s, 18.2, 3.8, 14, 1, "展望", size=15, bold=True, color=PRIMARY)
    text(s, 18.2, 5.1, 14, 7.5, [
        "课程模型经 IImageRecognizer 同接口接入，",
        "以独立真实图集评测（开发集定阈值、验证集出指标）",
        "移动端(MAUI)与消息推送",
        "厂区 Broker 对接（改连接即可）",
    ], size=13, line_spacing=1.6)

    # 10 结束
    s = add_slide(prs); bg(s, DARK)
    text(s, 2.5, 7.2, 29, 2.5, "谢谢，请老师提问", size=36, color=WHITE, bold=True, align=PP_ALIGN.CENTER)
    text(s, 2.5, 10.2, 29, 1.2, "演示账号：admin / warehouse / engineer / reporter（密码 123456）", size=14, color=RGBColor(0x9E, 0xC5, 0xFF), align=PP_ALIGN.CENTER)

    prs.save(OUT)
    print(f"√ 已生成：{OUT}")


if __name__ == "__main__":
    main()
