# -*- coding: utf-8 -*-
r"""《数据库表结构说明》生成器 —— 从真实数据库导出的结构元数据生成 Markdown + Word。

数据来源（docs/_schema/*.dat，UTF-8 BOM，分隔符 ‖）：
  sqlcmd -S "(localdb)\MSSQLLocalDB" -d IndustrialRepairDb -W -s "‖" -h -1 -f 65001 -Q "<查询>" -o <文件>
  五个查询：列结构 / 外键 / 索引 / 行数 / 视图-过程-触发器清单（见本文件末尾注释）。

用法：python docs/gen_table_doc.py   （先生成 dat，再跑本脚本）
输出：docs/数据库表结构说明.md + docs/数据库表结构说明.docx
"""
import docx
from docx.shared import Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DATA = ROOT / "_schema"

# ---------------------------------------------------------------- 表元数据（人工撰写）
# (表名, 中文名, 所属模块, 用途, 关键设计)
TABLE_META = {
    "sys_role":          ("角色字典", "M7 系统", "RBAC 四角色：管理员/库管员/工程师/报修人。", "角色码唯一；权限不直接挂用户而挂角色。"),
    "sys_user":          ("用户账号", "M1 认证", "登录账号与口令哈希（BCrypt cost=11）、所属角色、启用状态、找回密码邮箱。", "UserName 唯一过滤索引；失败 5 次锁定 10 分钟（服务层控制）。"),
    "sys_permission":    ("权限点字典", "M7 系统", "12 个功能权限点（页面/操作粒度），供角色映射与界面按钮显隐。", "PermCode 唯一；代码以常量引用，不硬编码字符串散落。"),
    "sys_role_permission": ("角色-权限映射", "M7 系统", "角色到权限点的多对多映射，登录后载入会话。", "复合主键 (RoleId, PermCode)；界面与服务层双层校验共用。"),
    "sys_login_log":     ("登录日志", "M1 认证", "每次登录尝试的时间、账号、来源 IPv4、结果（成功/口令错/锁定）。", "安全审计证据链；含失败记录。"),
    "sys_operation_log": ("操作日志", "M7 系统", "业务操作留痕（操作人/动作/对象/备注），触发器自动写入关键状态变化。", "与 sys_login_log 构成全链路审计。"),
    "part_category":     ("配件类别字典", "M2 配件", "配件分类（轴承/密封/电气…），配件与大屏类别统计的基础。", "CategoryCode 唯一。"),
    "part":              ("配件主数据", "M2 配件", "配件编码/名称/规格/单位/单价/库存/安全下限——识别映射、BOM 算料、条码查件三条链路的共同基础。", "PartCode 唯一；StockQty/MinStock/Price CHECK 非负。"),
    "device":            ("设备台账", "M4 设备", "设备编码/名称/所在车间/累计运行小时（Meter 型 PM 触发依据）。", "DeviceCode 唯一；RunningHours 由 MQTT 遥测累积更新。"),
    "bom":               ("设备 BOM", "M2 配件", "设备维修用料规则：单台基准用量（QtyPer）+ 冗余系数，算料公式 用量×(1+冗余) 向上取整。", "(DeviceId, PartId) 复合唯一，防重复行；QtyPer CHECK > 0。"),
    "work_order":        ("维修工单", "M3 工单", "业务主线：报修(照片/识别候选 JSON)→审阅→派单→接单→算料→出库→完工。", "状态机由存储过程承载；ScheduleId 关联 PM 自动建单；审阅快照字段固化历史。"),
    "work_order_record": ("工单流转记录", "M3 工单", "工单每次状态变化的时间线（创建/派单/接单/备注/完工/取消）。", "由触发器 trg_WorkOrder_Record 只记真实状态变化，非状态更新不留痕。"),
    "picking_list":      ("领料单汇总", "M5 领料", "工单生成的领料单：状态（Pending/Issued）、金额、审阅快照 JSON。", "一工单至多一张未出库单（唯一过滤索引 UQ_picking_pending_per_order 兜底并发）。"),
    "picking_item":      ("领料明细", "M5 领料", "领料单行：应发/实发数量、BOM 内★标记、快照价格。", "价格/数量取生成时快照，配件改价不影响历史单。"),
    "part_stock_log":    ("库存流水", "M2 配件", "每次入库/出库的数量与引用单号，消耗分析与 EOQ 的数据源。", "ChangeQty 含正负；EOQ 日需求标准差由此表按 CreatedAt 分组估计。"),
    "stock_alert":       ("库存预警", "M2 配件", "库存跌破安全下限的预警记录（触发器自动写入）。", "触发器 trg_Part_StockAlert 在扣减后判定。"),
    "schema_version":    ("结构版本登记", "M8 迁移", "版本化迁移的账本：每个迁移脚本成功应用后登记一行。", "同一事务内执行+登记，失败整体回滚版本号不落库。"),
    "maintenance_schedule": ("PM 预防性维护调度", "M6 维护", "按设备配置的周期性维护计划：Time(天数)/Meter(运行小时)双模式、模板描述与优先级。", "Quartz 每 30 分钟检查到期自动建工单并回写 ScheduleId 硬关联。"),
    "vendor":            ("供应商主数据", "M6 维护", "供应商名称/联系人/电话/邮箱/地址/启用状态。", "删除前须先解除 part_vendor 关联。"),
    "part_vendor":       ("配件-供应商关联", "M6 维护", "配件与供应商多对多：供货价/交期/是否首选——EOQ 建议直接消费这三个字段。", "(PartId, VendorId) 复合主键；首选优先、其次最便宜。"),
    "downtime_record":   ("停机记录", "M6 维护", "设备停机开始/结束/原因（固定七类）/操作人，OEE 可用率计算源。", "EndTime 可空=停机进行中；可用率=(周期-停机)/周期。"),
}

VIEW_META = {
    "vw_ConsumptionAnalysis": "配件消耗分析（年用量/近30日/总成本）——EOQ 优化的取数源",
    "vw_DeviceCost":          "设备维度维修成本汇总（报表页/设备成本 Tab）",
    "vw_DeviceDowntime":      "设备停机汇总（次数/总时长/平均/进行中）——OEE Tab",
    "vw_LowStock":            "低于安全下限的配件清单（大屏预警表/盘点）",
    "vw_OrderCost":           "工单材料成本（大屏高成本榜 TopN）",
    "vw_PickingDetail":       "领料明细宽表（导出 Excel/PDF）",
    "vw_RepairReport":        "月度维修报表（报表页主表）",
    "vw_WorkOrder_Full":      "工单宽表（列表页联查，替代多表 JOIN）",
}

PROC_META = {
    "usp_CreateOrder":        "创建工单（applock 取号，单号=前缀+日期+序号）",
    "usp_AssignOrder":        "派单（状态 Pending→Assigned，写流转）",
    "usp_TakeOrder":          "接单（Assigned→Working）",
    "usp_CompleteOrder":      "完工（Working→Done，校验材料已审阅）",
    "usp_CancelOrder":        "取消（非 Done 可取消，写原因）",
    "usp_AddNote":            "追加备注（不改状态，仅记录）",
    "usp_ReviewMaterials":    "材料审阅（PartConfirmed/BomOnly 两模式，审阅字段+快照同事务）",
    "usp_GeneratePicking":    "智能算料生成领料单（审阅闸门/整机BOM×冗余/★标记/快照/applock 单号）",
    "usp_IssuePicking":       "出库（UPDLOCK 行锁逐件扣减，不足整体回滚报缺口）",
    "usp_StockIn":            "入库（写流水，触发器判预警）",
    "usp_GetDashboard":       "大屏聚合（状态分布/近7日工单/成本/工程师排行，单次调用）",
    "usp_ListPartsPaged":     "配件分页列表（OFFSET-FETCH，模糊搜索）",
    "usp_ListWorkOrdersPaged":"工单分页列表（状态/关键字过滤）",
    "usp_ListPickingsPaged":  "领料单分页列表",
}

TRIG_META = {
    "trg_WorkOrder_Record":  "工单状态变化写流转记录（非状态更新不留痕）",
    "trg_Part_StockAlert":   "库存扣减后跌破下限自动写 stock_alert",
    "trg_PickingItem_Trace": "领料明细变化留痕（审计）",
}

MODULE_OF = {}  # 由 TABLE_META 填充

DELIM = "‖"

def load(name):
    rows = []
    p = DATA / name
    if not p.exists():
        raise SystemExit(f"缺少 {p}；请先按脚本头部注释生成 _schema 数据")
    for line in p.read_text(encoding="utf-8-sig").splitlines():
        line = line.rstrip()
        if not line or line.startswith("已影响") or "rows affected" in line:
            continue
        rows.append(line.split(DELIM))
    return rows

# ---------------------------------------------------------------- 数据组装
def nv(len_, ty):
    if ty == "nvarchar" and len_ != -1: return f"nvarchar({len_ // 2})"
    if ty == "nvarchar": return "nvarchar(max)"
    if ty == "varchar": return f"varchar({len_})" if len_ != -1 else "varchar(max)"
    if ty in ("decimal", "numeric"): return ty
    return ty

def main():
    cols = load("columns.dat")
    fks = load("fks.dat")
    idx = load("indexes.dat")
    counts = dict(load("counts.dat"))
    objs = load("objects.dat")

    tables = [t for t in cols if t]
    seen, table_order = set(), []
    for row in cols:
        if row[0] not in seen:
            seen.add(row[0]); table_order.append(row[0])
    table_order.sort(key=lambda t: list(TABLE_META).index(t) if t in TABLE_META else 999)

    # 列按表分组
    cols_by_table = {}
    for row in cols:
        t, cid, cname, ty, maxlen, nullable, ident = row
        cols_by_table.setdefault(t, []).append((int(cid), cname, ty, int(maxlen), nullable == "1", ident == "1"))

    # 外键按子表分组
    fk_by_table = {}
    for name, child, ccol, parent, pcol in fks:
        fk_by_table.setdefault(child, []).append((name, ccol, parent, pcol))

    # 索引按表分组
    ix_by_table = {}
    for t, name, pk, uq, filt, filtdef, ixcols in idx:
        ix_by_table.setdefault(t, []).append((name, pk == "1", uq == "1", filt == "1", filtdef, ixcols))

    # ================================================================ Markdown
    md = []
    md.append("# 数据库表结构说明\n")
    md.append("> 工业维修智能化零件分配平台（完整版）｜SQL Server LocalDB ｜本文档由 `docs/gen_table_doc.py` 从真实数据库结构自动生成\n")
    md.append("\n## 一、数据库概览\n")
    md.append("| 项目 | 内容 |")
    md.append("|---|---|")
    md.append("| 数据库名称 | IndustrialRepairDb（测试库独立命名，冒烟强制隔离） |")
    md.append("| 数据库系统 | SQL Server Express LocalDB（(localdb)\\MSSQLLocalDB） |")
    md.append("| 登录方式 | Windows 身份验证（Trusted_Connection），无需账号密码 |")
    md.append(f"| 业务表数量 | {len(table_order)} 张 |")
    md.append(f"| 视图 / 存储过程 / 触发器 | {sum(1 for r in objs if r[0].startswith('vw_'))} / {sum(1 for r in objs if r[0].startswith('usp_'))} / {sum(1 for r in objs if r[0].startswith('trg_'))} |")
    md.append("| 建库方式 | 程序启动时版本化迁移自动创建（DbInitializer，嵌入脚本+事务+失败整体回滚），无需手工建表 |")
    md.append("\n## 二、表清单\n")
    md.append("> 「当前行数」为业务库实测值（种子数据 + 演示数据）。\n")
    md.append("| 序号 | 表名 | 中文名 | 用途 | 模块 | 当前行数 |")
    md.append("|---|---|---|---|---|---|")
    for i, t in enumerate(table_order, 1):
        m = TABLE_META.get(t, (t, "-", "-", ""))
        md.append(f"| {i} | {t} | {m[0]} | {m[2]} | {m[1]} | {counts.get(t, '-')} |")

    md.append("\n## 三、表之间的关系\n")
    md.append("```\n"
              "sys_role ──< sys_role_permission >── sys_permission\n"
              "sys_role ──< sys_user ──< sys_login_log\n"
              "\n"
              "device ──┬──< bom >── part >── part_category\n"
              "         ├──< work_order（Reporter/Engineer → sys_user；ScheduleId → maintenance_schedule）\n"
              "         ├──< maintenance_schedule\n"
              "         └──< downtime_record\n"
              "\n"
              "work_order ──┬──< work_order_record\n"
              "             ├─── ConfirmedPartId → part（人工确认重点零件）\n"
              "             └──< picking_list ──< picking_item >── part\n"
              "\n"
              "part ──┬──< part_stock_log        （入库/出库流水 → EOQ 消耗分析）\n"
              "       ├──< stock_alert           （触发器自动预警）\n"
              "       └──< part_vendor >── vendor（供货价/交期/首选 → EOQ 建议）\n"
              "算料链路：识别候选(RecognizedJson) → 材料审阅(uspartReviewMaterials) → bom 用量×冗余\n"
              "         → picking_list/item 快照 → usp_IssuePicking 行锁扣减 part.StockQty\n"
              "```")
    md.append("\n### 外键清单\n")
    md.append("| 子表 | 子表字段 | 父表(字段) | 说明 |")
    md.append("|---|---|---|---|")
    fk_desc = {
        ("bom", "DeviceId"): "一套 BOM 属于一台设备",
        ("bom", "PartId"): "BOM 行引用配件，配件不可物理删除",
        ("downtime_record", "DeviceId"): "停机归属设备",
        ("maintenance_schedule", "DeviceId"): "PM 调度归属设备",
        ("part", "CategoryId"): "配件归属类别",
        ("part_stock_log", "PartId"): "流水归属配件",
        ("part_vendor", "PartId"): "配件-供应商关联",
        ("part_vendor", "VendorId"): "配件-供应商关联",
        ("picking_item", "PickingId"): "明细归属领料单",
        ("picking_item", "PartId"): "明细引用配件",
        ("picking_list", "OrderId"): "领料单归属工单",
        ("stock_alert", "PartId"): "预警归属配件",
        ("sys_role_permission", "RoleId"): "角色-权限映射",
        ("sys_role_permission", "PermCode"): "角色-权限映射",
        ("sys_user", "RoleId"): "用户归属角色",
        ("work_order", "DeviceId"): "工单报修设备",
        ("work_order", "ReporterId"): "报修人",
        ("work_order", "EngineerId"): "获派工程师",
        ("work_order", "ConfirmedPartId"): "审阅确认的重点零件",
        ("work_order", "ReviewedBy"): "审阅人",
        ("work_order", "ScheduleId"): "PM 自动建单的硬关联（Schema v5）",
        ("work_order_record", "OrderId"): "流转记录归属工单",
    }
    for _, child, ccol, parent, pcol in fks:
        md.append(f"| {child} | {ccol} | {parent}({pcol}) | {fk_desc.get((child, ccol), '')} |")
    md.append("\n> **刻意未建外键**：`picking_item.PartId` 不加 FK 约束的审慎设计——领料明细以生成时快照（编码/名称/规格/价格）留档，"
              "配件停用或删除后历史清单仍可完整读取；`work_order.ConfirmedPartId` 为可空弱引用，未审阅工单该列为空。\n")

    md.append("\n## 四、逐表明细\n")
    for t in table_order:
        m = TABLE_META.get(t, (t, "-", "-", ""))
        md.append(f"### {t}（{m[0]}）\n")
        md.append(f"所属模块：{m[1]}　　当前行数：{counts.get(t, '-')}\n")
        md.append(f"{m[2]}\n")
        if m[3]:
            md.append(f"**关键设计**：{m[3]}\n")
        md.append("| 列 | 类型 | 空 | 标识 |")
        md.append("|---|---|---|---|")
        for cid, cname, ty, maxlen, nullable, ident in sorted(cols_by_table[t]):
            md.append(f"| {cname} | {nv(maxlen, ty)} | {'√' if nullable else ''} | {'√' if ident else ''} |")
        ixs = ix_by_table.get(t, [])
        if ixs:
            md.append("\n**键与索引**\n")
            for name, pk, uq, filt, filtdef, ixcols in ixs:
                kind = "主键" if pk else ("唯一" if uq else "索引")
                extra = f"（过滤：{filtdef}）" if filt else ""
                md.append(f"- {kind} {name} ({ixcols}){extra}")
        md.append("")

    md.append("\n## 五、视图 / 存储过程 / 触发器\n")
    md.append("### 视图（8）\n")
    md.append("| 视图 | 用途 |")
    md.append("|---|---|")
    for r in objs:
        if r[0].startswith("vw_"):
            md.append(f"| {r[0]} | {VIEW_META.get(r[0], '')} |")
    md.append("\n### 存储过程（14）\n")
    md.append("> 业务状态机全部由存储过程承载（参数化，服务层仅编排与鉴权）。\n")
    md.append("| 存储过程 | 用途 |")
    md.append("|---|---|")
    for r in objs:
        if r[0].startswith("usp_"):
            md.append(f"| {r[0]} | {PROC_META.get(r[0], '')} |")
    md.append("\n### 触发器（3）\n")
    md.append("| 触发器 | 用途 |")
    md.append("|---|---|")
    for r in objs:
        if r[0].startswith("trg_"):
            md.append(f"| {r[0]} | {TRIG_META.get(r[0], '')} |")
    md.append("\n---\n*本文档由 docs/gen_table_doc.py 自动生成，表结构变更后重跑即可同步。*\n")

    (ROOT / "数据库表结构说明.md").write_text("\n".join(md), encoding="utf-8")

    # ================================================================ Word
    doc = docx.Document()
    style = doc.styles["Normal"]
    style.font.name = "宋体"; style.font.size = Pt(12)
    style.element.rPr.rFonts.set(docx.oxml.ns.qn("w:eastAsia"), "宋体")

    h = doc.add_heading("工业维修智能化零件分配平台（完整版）数据库表结构说明", level=0)
    h.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p = doc.add_paragraph("本文档由 docs/gen_table_doc.py 从真实数据库结构自动生成。")
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER

    doc.add_heading("一、数据库概览", level=1)
    ov = doc.add_table(rows=1, cols=2); ov.style = "Table Grid"
    ov.rows[0].cells[0].text = "项目"; ov.rows[0].cells[1].text = "内容"
    overview = [
        ("数据库名称", "IndustrialRepairDb（测试库独立命名，冒烟强制隔离）"),
        ("数据库系统", "SQL Server Express LocalDB（(localdb)\\MSSQLLocalDB）"),
        ("登录方式", "Windows 身份验证（Trusted_Connection）"),
        ("业务表数量", f"{len(table_order)} 张"),
        ("视图/存储过程/触发器", "8 / 14 / 3"),
        ("建库方式", "程序启动版本化迁移自动创建（事务 + 失败整体回滚）"),
    ]
    for k, v in overview:
        r = ov.add_row(); r.cells[0].text = k; r.cells[1].text = v

    doc.add_heading("二、表清单", level=1)
    tl = doc.add_table(rows=1, cols=6); tl.style = "Table Grid"
    for i, htxt in enumerate(["序号", "表名", "中文名", "用途", "模块", "行数"]):
        tl.rows[0].cells[i].text = htxt
    for i, t in enumerate(table_order, 1):
        m = TABLE_META.get(t, (t, "-", "-", ""))
        r = tl.add_row()
        for j, v in enumerate([str(i), t, m[0], m[2], m[1], counts.get(t, "-")]):
            r.cells[j].text = v

    doc.add_heading("三、表关系与外键", level=1)
    doc.add_paragraph("22 个外键维护引用完整性；picking_item.PartId 刻意不建外键（历史清单以快照留档，配件删除后仍可读取）。"
                      "算料链路：识别候选 → 材料审阅 → BOM×冗余 → 领料快照 → 行锁出库。")
    fkt = doc.add_table(rows=1, cols=4); fkt.style = "Table Grid"
    for i, htxt in enumerate(["子表", "子表字段", "父表(字段)", "说明"]):
        fkt.rows[0].cells[i].text = htxt
    for _, child, ccol, parent, pcol in fks:
        r = fkt.add_row()
        r.cells[0].text = child; r.cells[1].text = ccol
        r.cells[2].text = f"{parent}({pcol})"
        r.cells[3].text = fk_desc.get((child, ccol), "")

    doc.add_heading("四、逐表明细", level=1)
    for t in table_order:
        m = TABLE_META.get(t, (t, "-", "-", ""))
        doc.add_heading(f"{t}（{m[0]}）", level=2)
        doc.add_paragraph(f"模块：{m[1]}｜行数：{counts.get(t, '-')}　{m[2]}")
        if m[3]:
            doc.add_paragraph(f"关键设计：{m[3]}")
        ct = doc.add_table(rows=1, cols=4); ct.style = "Table Grid"
        for i, htxt in enumerate(["列", "类型", "空", "标识"]):
            ct.rows[0].cells[i].text = htxt
        for cid, cname, ty, maxlen, nullable, ident in sorted(cols_by_table[t]):
            r = ct.add_row()
            r.cells[0].text = cname; r.cells[1].text = nv(maxlen, ty)
            r.cells[2].text = "√" if nullable else ""; r.cells[3].text = "√" if ident else ""
        ixs = ix_by_table.get(t, [])
        if ixs:
            keys = "；".join(
                ("主键 " if pk else "唯一 " if uq else "") + f"{name}({ixcols})" + (f" 过滤:{filtdef}" if filt else "")
                for name, pk, uq, filt, filtdef, ixcols in ixs if pk or uq)
            plain = "；".join(f"{name}({ixcols})" for name, pk, uq, filt, filtdef, ixcols in ixs if not pk and not uq)
            if keys: doc.add_paragraph("键：" + keys)
            if plain: doc.add_paragraph("索引：" + plain)

    doc.add_heading("五、视图 / 存储过程 / 触发器", level=1)
    for title, prefix, meta in [("视图", "vw_", VIEW_META), ("存储过程", "usp_", PROC_META), ("触发器", "trg_", TRIG_META)]:
        doc.add_heading(title, level=2)
        ot = doc.add_table(rows=1, cols=2); ot.style = "Table Grid"
        ot.rows[0].cells[0].text = title; ot.rows[0].cells[1].text = "用途"
        for r0 in objs:
            if r0[0].startswith(prefix):
                r = ot.add_row(); r.cells[0].text = r0[0]; r.cells[1].text = meta.get(r0[0], "")

    doc.save(ROOT / "数据库表结构说明.docx")
    print("√ 已生成：数据库表结构说明.md / .docx")

if __name__ == "__main__":
    main()

# ---------------------------------------------------------------- _schema 生成命令（手工执行）
# columns.dat: SELECT t.name,c.column_id,c.name,TYPE_NAME(c.user_type_id),c.max_length,c.is_nullable,CASE WHEN ic.object_id IS NOT NULL THEN 1 ELSE 0 END FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id ORDER BY t.name,c.column_id
# fks.dat:     SELECT fk.name,OBJECT_NAME(fk.parent_object_id),COL_NAME(fkc.parent_object_id,fkc.parent_column_id),OBJECT_NAME(fk.referenced_object_id),COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id) FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id ORDER BY 2
# indexes.dat: SELECT OBJECT_NAME(i.object_id),i.name,i.is_primary_key,i.is_unique,i.has_filter,i.filter_definition,STUFF((SELECT ','+c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=0 ORDER BY ic.key_ordinal FOR XML PATH('')),1,1,'') FROM sys.indexes i WHERE i.index_id>0 AND i.is_hypothetical=0 AND OBJECTPROPERTY(i.object_id,'IsUserTable')=1 ORDER BY 1,i.index_id
# counts.dat:  SELECT t.name,p.rows FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN(0,1) ORDER BY t.name
# objects.dat: SELECT name FROM sys.views ORDER BY name; SELECT name,type_desc FROM sys.procedures WHERE OBJECTPROPERTY(object_id,'IsMSShipped')=0 ORDER BY name; SELECT name FROM sys.triggers ORDER BY name
