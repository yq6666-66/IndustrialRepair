# -*- coding: utf-8 -*-
"""两遍法预填目录页码（任务书要求"目录中的页码也要写好"）。

流程：导出 docx（占位目录）→ LibreOffice 转 PDF → pdftotext 逐页抽取标题所在页
      → 写 toc_pages.json（打印页码 = PDF 页 - 2，因封面/目录不编号）→ 重新导出。

用法：python docs/refresh_toc_pages.py
依赖：LibreOffice 与 poppler（pdftotext）已安装。
注意：正文内容改动后需重跑本脚本；仅填封面学号/姓名不影响页码。
"""
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DOCX = ROOT / "实训报告_工业维修智能化零件分配平台.docx"
JSON = ROOT / "toc_pages.json"
SOFFICE = r"C:\Program Files\LibreOffice\program\soffice.exe"

# （层级, 标题；用前缀匹配 PDF 行首，避免全角括号/引号字形差异）
HEADINGS = [
    (1, "一、目的与意义"), (2, "1.1 实训目的"), (2, "1.2 学习内容与完成情况"),
    (1, "二、需求分析"),
    (1, "三、系统设计"), (2, "3.1 总体架构"), (2, "3.2 数据库设计"),
    (2, "3.3 关键数据库对象"), (2, "3.4 拍照识别方案"),
    (1, "四、详细实现"), (2, "4.1 开源组件集成"),
    (1, "五、运行结果"),
    (1, "六、系统测试"),
    (1, "七、部署与运行"),
    (1, "八、总结"),
    (1, "参考文献"),
]


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True,
                          encoding="utf-8", errors="replace")


def norm(s):
    return re.sub(r"\s+", "", s)


def main():
    py = sys.executable
    # 第 1 遍：无目录缓存导出（占位目录）
    JSON.unlink(missing_ok=True)
    r = run([py, str(ROOT / "export_report.py")])
    if r.returncode:
        sys.exit(f"导出失败：{r.stdout}\n{r.stderr}")
    print(r.stdout.strip())

    # 转 PDF
    tmp = Path(tempfile.mkdtemp(prefix="toc_"))
    r = run([SOFFICE, "--headless", "--convert-to", "pdf", "--outdir", str(tmp), str(DOCX)])
    pdf = next(tmp.glob("*.pdf"), None)
    if pdf is None:
        sys.exit(f"PDF 转换失败：{r.stdout}\n{r.stderr}")

    # 逐页抽文本
    pages, n = {}, 0
    while True:
        n += 1
        r = run(["pdftotext", "-enc", "UTF-8", "-f", str(n), "-l", str(n), str(pdf), "-"])
        if r.returncode != 0 or not r.stdout.strip():
            if n == 1:
                sys.exit("pdftotext 不可用或 PDF 无文本层")
            break
        pages[n] = r.stdout
    total = n - 1
    print(f"PDF 共 {total} 页；封面=1、目录=2 不编号，正文首页 PDF 第 3 页 = 页码 1")

    # 顺序匹配标题行首（跳过封面/目录两页）
    entries, cur = [], 3
    for level, text in HEADINGS:
        key = norm(text)
        found = None
        for pno in range(cur, total + 1):
            for line in pages.get(pno, "").splitlines():
                if norm(line).startswith(key):
                    found = pno
                    break
            if found:
                break
        if not found:
            sys.exit(f"未在 PDF 中找到标题：{text}")
        cur = found
        entries.append({"level": level, "text": text, "page": found - 2})
    JSON.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"√ 已写 {JSON.name}（{len(entries)} 条）")

    # 第 2 遍：带页码目录重新导出
    r = run([py, str(ROOT / "export_report.py")])
    if r.returncode:
        sys.exit(f"二次导出失败：{r.stdout}\n{r.stderr}")
    print(r.stdout.strip())
    print("完成：docx 目录已带页码（Word 中仍可 Ctrl+A → F9 重建）。")


if __name__ == "__main__":
    main()
