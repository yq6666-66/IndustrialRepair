# -*- coding: utf-8 -*-
"""实训报告草稿.md → 按任务书格式排版的 Word（docs/实训报告_工业维修智能化零件分配平台.docx）

格式（任务书硬性要求）：
  一级标题：黑体·小二(18pt)，段前段后 1 行
  二级标题：黑体·小三(15pt)
  三级标题：黑体·小四(12pt)
  正文：    宋体·小四(12pt)，行距固定值 28 磅，首行缩进 2 字符
  表格：    宋体·五号(10.5pt)，表头加粗
  目录：    插入 TOC 域（打开后 右键→更新域 生成带页码目录）
用法：python docs/export_report.py
"""
import re
import sys
from pathlib import Path

from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_LINE_SPACING
from docx.oxml.ns import qn
from docx.oxml import OxmlElement
from docx.shared import Pt, Cm

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "实训报告草稿.md"
OUT = ROOT / "实训报告_工业维修智能化零件分配平台.docx"

HEI, SONG = "黑体", "宋体"


def set_font(run, name=SONG, size=12, bold=False):
    run.font.name = name
    run.font.size = Pt(size)
    run.font.bold = bold
    run._element.rPr.rFonts.set(qn("w:eastAsia"), name)


def fixed_line_spacing(par, pts=28):
    par.paragraph_format.line_spacing_rule = WD_LINE_SPACING.EXACTLY
    par.paragraph_format.line_spacing = Pt(pts)


def add_toc(par):
    """插入 TOC 域：Word 打开后 F9/右键更新生成带页码目录。"""
    r = par.add_run()
    fld = OxmlElement("w:fldChar"); fld.set(qn("w:fldCharType"), "begin")
    instr = OxmlElement("w:instrText"); instr.set(qn("xml:space"), "preserve")
    instr.text = r'TOC \o "1-2" \h \z \u'
    sep = OxmlElement("w:fldChar"); sep.set(qn("w:fldCharType"), "separate")
    tip = OxmlElement("w:t"); tip.text = "（目录域：请在 Word 中右键→更新域，生成带页码目录）"
    end = OxmlElement("w:fldChar"); end.set(qn("w:fldCharType"), "end")
    for el in (fld, instr, sep, tip, end):
        r._r.append(el)


INLINE = re.compile(r"(\*\*.+?\*\*|`[^`]+`)")


def add_body(doc, text):
    par = doc.add_paragraph()
    par.paragraph_format.first_line_indent = Pt(24)  # 小四 2 字符
    fixed_line_spacing(par)
    for part in INLINE.split(text):
        if not part:
            continue
        if part.startswith("**") and part.endswith("**"):
            set_font(par.add_run(part[2:-2]), bold=True)
        elif part.startswith("`") and part.endswith("`"):
            run = par.add_run(part[1:-1]); run.font.name = "Consolas"
            run.font.size = Pt(10.5)
            run._element.rPr.rFonts.set(qn("w:eastAsia"), SONG)
        else:
            set_font(par.add_run(part))


def add_table(doc, rows):
    cells = [ [c.strip() for c in r.strip().strip("|").split("|")] for r in rows ]
    ncol = max(len(r) for r in cells)
    table = doc.add_table(rows=len(cells), cols=ncol)
    table.style = "Table Grid"
    for i, row in enumerate(cells):
        for j in range(ncol):
            txt = row[j] if j < len(row) else ""
            cell = table.cell(i, j)
            cell.text = ""
            par = cell.paragraphs[0]
            run = par.add_run(re.sub(r"\*\*|`", "", txt))
            set_font(run, size=10.5, bold=(i == 0))
    # 表后空段
    sp = doc.add_paragraph(); fixed_line_spacing(sp, 12)


def main():
    if not SRC.exists():
        sys.exit(f"未找到 {SRC}")
    doc = Document()
    sec = doc.sections[0]
    sec.page_width, sec.page_height = Cm(21.0), Cm(29.7)          # A4
    for attr in ("left_margin", "right_margin"):
        setattr(sec, attr, Cm(3.0))
    sec.top_margin = sec.bottom_margin = Cm(2.5)

    # ---- 封面 ----
    def cover(text, size, bold=True, before=0, after=0):
        p = doc.add_paragraph()
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.space_before = Pt(before)
        p.paragraph_format.space_after = Pt(after)
        set_font(p.add_run(text), HEI, size, bold)

    for _ in range(4):
        cover("", 16, False)
    cover("《软件工程综合实训》", 26, True, after=6)
    cover("课程设计报告", 26, True, after=24)
    cover("工业维修智能化零件分配平台", 22, True, after=36)
    cover("班级：软件工程 23 级（E511/E512）", 14, False, after=8)
    cover("学号：________________", 14, False, after=8)
    cover("姓名：________________", 14, False, after=8)
    cover("指导教师：________________", 14, False, after=8)
    cover("日期：2026 年 10 月", 14, False)
    doc.add_page_break()

    # ---- 目录 ----
    h = doc.add_paragraph()
    h.alignment = WD_ALIGN_PARAGRAPH.CENTER
    set_font(h.add_run("目　　录"), HEI, 16)
    add_toc(doc.add_paragraph())
    doc.add_page_break()

    # ---- 正文（解析 Markdown）----
    lines = SRC.read_text(encoding="utf-8").splitlines()
    i = 0
    while i < len(lines):
        line = lines[i].rstrip()
        if not line.strip() or line.strip() == "---":
            i += 1; continue
        if line.startswith(">"):          # 引言块 → 灰色小注
            par = doc.add_paragraph()
            fixed_line_spacing(par, 24)
            set_font(par.add_run(line.lstrip("> ").strip()), size=9)
            i += 1; continue
        if line.startswith("|"):          # 表格
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                if not re.match(r"^\|[\s:\-|]+\|$", lines[i].strip()):
                    rows.append(lines[i])
                i += 1
            add_table(doc, rows)
            continue
        if line.startswith("# "):
            # 文档主标题（封面已含，正文不再重复）；如需保留可改小二居中
            i += 1; continue
        if line.startswith("## "):
            p = doc.add_paragraph()
            p.paragraph_format.space_before = Pt(14)
            p.paragraph_format.space_after = Pt(8)
            set_font(p.add_run(line[3:].strip()), HEI, 18)      # 一级：黑体·小二
            i += 1; continue
        if line.startswith("### "):
            p = doc.add_paragraph()
            p.paragraph_format.space_before = Pt(10)
            p.paragraph_format.space_after = Pt(4)
            set_font(p.add_run(line[4:].strip()), HEI, 15)      # 二级：黑体·小三
            i += 1; continue
        if line.startswith("#### "):
            p = doc.add_paragraph()
            fixed_line_spacing(p)
            set_font(p.add_run(line[5:].strip()), HEI, 12)      # 三级：黑体·小四
            i += 1; continue
        if re.match(r"^[-*] ", line) or re.match(r"^\d+\. ", line):
            par = doc.add_paragraph(style="List Bullet" if line.startswith(("-", "*")) else "List Number")
            fixed_line_spacing(par)
            txt = re.sub(r"^([-*]|\d+\.)\s+", "", line)
            for part in INLINE.split(txt):
                if part:
                    set_font(par.add_run(part.strip("*`")))
            i += 1; continue
        add_body(doc, line)
        i += 1

    doc.save(OUT)
    print(f"√ 已生成：{OUT}")
    print("  打开后：Ctrl+A → F9 更新目录域；填写封面姓名/学号。")


if __name__ == "__main__":
    main()
