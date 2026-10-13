# -*- coding: utf-8 -*-
"""把 docs/ 下的定稿 markdown 构建为 docx/ 下的 .docx。

用法（使用 DSH 自带 python）：
    python docx/_build_docx.py

约定：
- 源 markdown 是**定稿文档**，统一放在仓库 `docs/` 下，命名 `<主题>.md`（契约）或
  `<主题>-review.md`（意见稿）；本目录只保留构建脚本与生成物。
- 生成物（.docx）与校验结果（_checks.json）不入库，见根 `.gitignore`。
- 中文字形需要显式设置 w:eastAsia，否则 Word 里可能回退到无中文字形的字体。
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent
# 源 markdown 已迁入仓库 docs/，这里的路径必须与之一致；
# 2026-10-13 文档基线重构时旧文件 `docx/低代码逻辑编排-完善意见稿.md` 已删，故此处置新。
SRC = REPO / "docs" / "lowcode-logic-orchestration-review.md"
OUT = ROOT / "低代码逻辑编排-完善意见稿.docx"

if not SRC.exists():
    raise SystemExit(
        f"源 markdown 不存在：{SRC}\n"
        "文档基线重构后源文件已迁到 docs/，请确认 docs/lowcode-logic-orchestration-review.md 存在。"
    )

LATIN_FONT = "Segoe UI"
EASTASIA_FONT = "Microsoft YaHei"
MONO_FONT = "Consolas"

BODY_SIZE = Pt(10)
TABLE_SIZE = Pt(8.5)
QUOTE_SIZE = Pt(9)


def set_run_fonts(run, latin=LATIN_FONT, eastasia=EASTASIA_FONT, size=None, mono=False):
    if mono:
        latin, eastasia = MONO_FONT, MONO_FONT
    run.font.name = latin
    rpr = run._element.get_or_add_rPr()
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.append(rfonts)
    rfonts.set(qn("w:ascii"), latin)
    rfonts.set(qn("w:hAnsi"), latin)
    rfonts.set(qn("w:eastAsia"), eastasia)
    if size is not None:
        run.font.size = size


def style_fonts(style, latin=LATIN_FONT, eastasia=EASTASIA_FONT):
    style.font.name = latin
    rpr = style.element.get_or_add_rPr()
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.append(rfonts)
    rfonts.set(qn("w:ascii"), latin)
    rfonts.set(qn("w:hAnsi"), latin)
    rfonts.set(qn("w:eastAsia"), eastasia)


def add_page_number_footer(section):
    footer = section.footer
    para = footer.paragraphs[0] if footer.paragraphs else footer.add_paragraph()
    para.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = para.add_run()
    set_run_fonts(run, size=Pt(8), eastasia=EASTASIA_FONT)
    fld_begin = OxmlElement("w:fldChar")
    fld_begin.set(qn("w:fldCharType"), "begin")
    instr = OxmlElement("w:instrText")
    instr.set(qn("xml:space"), "preserve")
    instr.text = "PAGE"
    fld_end = OxmlElement("w:fldChar")
    fld_end.set(qn("w:fldCharType"), "end")
    run._element.append(fld_begin)
    run._element.append(instr)
    run._element.append(fld_end)


LINK_RE = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")
TOKEN_RE = re.compile(r"(\*\*.+?\*\*|`[^`]+`)")


def add_inline(paragraph, text, size=BODY_SIZE, base_bold=False, mono_all=False):
    """写入一行文本，处理 **粗体**、`等宽`、[文字](链接)。"""
    text = LINK_RE.sub(lambda m: f"{m.group(1)}（{m.group(2)}）", text)
    for chunk in TOKEN_RE.split(text):
        if not chunk:
            continue
        if chunk.startswith("**") and chunk.endswith("**") and len(chunk) > 4:
            run = paragraph.add_run(chunk[2:-2])
            run.bold = True
            set_run_fonts(run, size=size, mono=mono_all)
        elif chunk.startswith("`") and chunk.endswith("`") and len(chunk) > 2:
            run = paragraph.add_run(chunk[1:-1])
            set_run_fonts(run, size=Pt(size.pt - 0.5), mono=True)
        else:
            run = paragraph.add_run(chunk)
            run.bold = base_bold
            set_run_fonts(run, size=size, mono=mono_all)


def split_row(line: str):
    line = line.strip()
    if line.startswith("|"):
        line = line[1:]
    if line.endswith("|"):
        line = line[:-1]
    return [c.strip() for c in line.split("|")]


def is_separator_row(line: str) -> bool:
    cells = split_row(line)
    return bool(cells) and all(re.fullmatch(r":?-{2,}:?", c or "-") for c in cells)


def set_fixed_layout(table, rows, ncols, usable_cm=17.2):
    """按各列内容长度分配固定列宽，避免长单元格（如 `路径:行号`）把某列撑爆。"""
    weights = []
    for c in range(ncols):
        lens = [len((rows[r][c] if c < len(rows[r]) else "")) for r in range(len(rows))]
        mean = sum(lens) / max(1, len(lens))
        weights.append(min(max(mean, 4.0), 60.0))
    total_w = sum(weights)
    widths = [Cm(usable_cm * w / total_w) for w in weights]

    table.autofit = False
    tblpr = table._tbl.tblPr
    layout = OxmlElement("w:tblLayout")
    layout.set(qn("w:type"), "fixed")
    tblpr.append(layout)
    for row in table.rows:
        for idx, cell in enumerate(row.cells):
            if idx < len(widths):
                cell.width = widths[idx]
    for idx, col in enumerate(table.columns):
        if idx < len(widths):
            col.width = widths[idx]


def build():
    lines = SRC.read_text(encoding="utf-8").splitlines()

    doc = Document()
    section = doc.sections[0]
    section.page_width = Cm(21.0)
    section.page_height = Cm(29.7)
    for attr, value in (("top_margin", 2.0), ("bottom_margin", 2.0),
                        ("left_margin", 1.9), ("right_margin", 1.9)):
        setattr(section, attr, Cm(value))

    normal = doc.styles["Normal"]
    style_fonts(normal)
    normal.font.size = BODY_SIZE
    normal.paragraph_format.space_after = Pt(4)
    normal.paragraph_format.line_spacing = 1.25
    for name, size, color in (
        ("Heading 1", 16, RGBColor(0x1F, 0x38, 0x64)),
        ("Heading 2", 13, RGBColor(0x1F, 0x38, 0x64)),
        ("Heading 3", 11.5, RGBColor(0x2E, 0x54, 0x96)),
        ("Heading 4", 10.5, RGBColor(0x2E, 0x54, 0x96)),
    ):
        st = doc.styles[name]
        style_fonts(st)
        st.font.size = Pt(size)
        st.font.color.rgb = color
        st.font.bold = True

    add_page_number_footer(section)

    i = 0
    total = len(lines)
    while i < total:
        raw = lines[i]
        line = raw.rstrip()
        stripped = line.strip()

        if not stripped:
            i += 1
            continue

        # 水平线：跳过（标题已有层级）
        if re.fullmatch(r"-{3,}", stripped):
            i += 1
            continue

        # 表格
        if stripped.startswith("|"):
            block = []
            while i < total and lines[i].strip().startswith("|"):
                block.append(lines[i])
                i += 1
            rows = [split_row(b) for b in block if not is_separator_row(b)]
            if not rows:
                continue
            ncols = max(len(r) for r in rows)
            table = doc.add_table(rows=0, cols=ncols)
            table.style = "Table Grid"
            table.alignment = WD_TABLE_ALIGNMENT.CENTER
            table.autofit = True
            for ridx, cells in enumerate(rows):
                row = table.add_row()
                for cidx in range(ncols):
                    cell = row.cells[cidx]
                    cell.text = ""
                    para = cell.paragraphs[0]
                    para.paragraph_format.space_after = Pt(1)
                    para.paragraph_format.line_spacing = 1.05
                    value = cells[cidx] if cidx < len(cells) else ""
                    add_inline(para, value, size=TABLE_SIZE,
                               base_bold=(ridx == 0))
                if ridx == 0:  # 表头跨页重复
                    trpr = row._tr.get_or_add_trPr()
                    hdr = OxmlElement("w:tblHeader")
                    hdr.set(qn("w:val"), "true")
                    trpr.append(hdr)
            set_fixed_layout(table, rows, ncols)
            doc.add_paragraph()
            continue

        # 引用块
        if stripped.startswith(">"):
            buf = []
            while i < total and lines[i].strip().startswith(">"):
                buf.append(lines[i].strip().lstrip(">").strip())
                i += 1
            para = doc.add_paragraph()
            para.paragraph_format.left_indent = Cm(0.5)
            para.paragraph_format.space_before = Pt(2)
            para.paragraph_format.space_after = Pt(6)
            add_inline(para, " ".join(x for x in buf if x), size=QUOTE_SIZE)
            for run in para.runs:
                run.italic = True
            continue

        # 标题
        m = re.match(r"^(#{1,6})\s+(.*)$", stripped)
        if m:
            level = min(len(m.group(1)), 4)
            text = m.group(2).strip()
            if level == 1 and not any(p.style.name.startswith("Heading") for p in doc.paragraphs):
                para = doc.add_paragraph(style="Title")
                style_fonts(doc.styles["Title"])
                add_inline(para, text, size=Pt(20))
            else:
                para = doc.add_heading(level=level)
                add_inline(para, text, size=doc.styles[f"Heading {level}"].font.size)
            i += 1
            continue

        # 列表
        m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", line)
        if m:
            indent = len(m.group(1))
            style = "List Number" if m.group(2)[0].isdigit() else "List Bullet"
            para = doc.add_paragraph(style=style)
            if indent >= 2:
                para.paragraph_format.left_indent = Cm(1.2)
            para.paragraph_format.space_after = Pt(2)
            add_inline(para, m.group(3).strip())
            i += 1
            continue

        # 普通段落
        para = doc.add_paragraph()
        add_inline(para, stripped)
        i += 1

    doc.save(OUT)
    return OUT


if __name__ == "__main__":
    out = build()
    print(f"OK -> {out}")
    sys.exit(0)
