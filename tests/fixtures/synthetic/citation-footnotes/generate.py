"""Issue #153 注释体例检查的合成样本（零依赖）。

两份样本都是最小 docx，只保留本条检查真正会读的三个部件：
word/document.xml（正文与 sectPr）、word/settings.xml（文档级 footnotePr）、
word/footnotes.xml（脚注正文）。内容是人工构造的，不是真实论文。

* footnote_compliant.docx   —— 南开口径：圈号序号（decimalEnclosedCircle）、
  eachPage 重编号、注释走 w:footnoteReference、脚注里不塞 [n]。预期 0 条问题。
* footnote_noncompliant.docx —— 四条各命中一次：正文写随文注、numFmt 是 decimal、
  缺 numRestart、脚注正文里出现 [1] 与正文引用混用。预期 4 条问题。

只用标准库。条目顺序与 date_time 固定为 FIXED_TIME，与 full-thesis/ 一致，
`--check` 直接比整文件字节，不退化成逐部件比对。

用法（在本目录下）：
    python generate.py            # 重新生成两份样本
    python generate.py --check    # 只比对，不写盘
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from io import BytesIO
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

HERE = Path(__file__).resolve().parent

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PKG_REL = "http://schemas.openxmlformats.org/package/2006/relationships"
CT = "http://schemas.openxmlformats.org/package/2006/content-types"

# 与 full-thesis/、format-samples/ 一致：ZIP 可存的最早时间。
FIXED_TIME = (2000, 1, 1, 0, 0, 0)

COMPLIANT = "footnote_compliant.docx"
NONCOMPLIANT = "footnote_noncompliant.docx"
SAMPLES = (COMPLIANT, NONCOMPLIANT)

CONTENT_TYPES = (
    f'<Types xmlns="{CT}">'
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
    '<Default Extension="xml" ContentType="application/xml"/>'
    '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
    '<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>'
    '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>'
    '<Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/>'
    '</Types>'
)

ROOT_RELS = (
    f'<Relationships xmlns="{PKG_REL}">'
    f'<Relationship Id="rId1" Type="{R}/officeDocument" Target="word/document.xml"/>'
    '</Relationships>'
)

DOCUMENT_RELS = (
    f'<Relationships xmlns="{PKG_REL}">'
    f'<Relationship Id="rId1" Type="{R}/styles" Target="styles.xml"/>'
    f'<Relationship Id="rId2" Type="{R}/footnotes" Target="footnotes.xml"/>'
    '</Relationships>'
)

STYLES = (
    f'<w:styles xmlns:w="{W}">'
    '<w:docDefaults><w:rPrDefault><w:rPr/></w:rPrDefault></w:docDefaults>'
    '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>'
    '</w:styles>'
)


def _paragraph(text: str, *, note_id: int | None = None) -> str:
    anchor = f'<w:r><w:footnoteReference w:id="{note_id}"/></w:r>' if note_id is not None else ""
    return f'<w:p><w:r><w:t>{text}</w:t></w:r>{anchor}</w:p>'


def _document_xml(*, compliant: bool) -> str:
    if compliant:
        paragraphs = (
            _paragraph("第一章 绪论。正文引用了一篇参考文献[1]。")
            + _paragraph("此处概念需要补充说明，详见页下注", note_id=2)
            + _paragraph("其余正文段落不含注释标记。")
        )
        number_format = 'decimalEnclosedCircle'
        restart = '<w:numRestart w:val="eachPage"/>'
    else:
        paragraphs = (
            _paragraph("第一章 绪论。正文引用了一篇参考文献[1]。")
            + _paragraph("此处概念需要补充说明（注：说明文字直接写在正文括号里）。")
            + _paragraph("另一处注释仍走脚注", note_id=3)
        )
        number_format = 'decimal'
        restart = ''

    footnote_pr = f'<w:footnotePr><w:numFmt w:val="{number_format}"/>{restart}</w:footnotePr>'
    return (
        f'<w:document xmlns:w="{W}"><w:body>'
        f'{paragraphs}'
        f'<w:sectPr>{footnote_pr}<w:pgSz w:w="11906" w:h="16838"/></w:sectPr>'
        '</w:body></w:document>'
    )


def _settings_xml(*, compliant: bool) -> str:
    number_format = "decimalEnclosedCircle" if compliant else "decimal"
    restart = '<w:numRestart w:val="eachPage"/>' if compliant else ""
    return (
        f'<w:settings xmlns:w="{W}">'
        f'<w:footnotePr><w:numFmt w:val="{number_format}"/>{restart}</w:footnotePr>'
        '</w:settings>'
    )


def _footnotes_xml(*, compliant: bool) -> str:
    note_id = 2 if compliant else 3
    note_text = "① 概念出自某规范，正文从略。" if compliant else "1. 说明中另引了文献[1]，与正文编号混用。"
    return (
        f'<w:footnotes xmlns:w="{W}">'
        f'<w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>'
        f'<w:footnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>'
        f'<w:footnote w:id="{note_id}"><w:p><w:r><w:footnoteRef/></w:r>'
        f'<w:r><w:t>{note_text}</w:t></w:r></w:p></w:footnote>'
        '</w:footnotes>'
    )


def _parts(name: str) -> list[tuple[str, str]]:
    compliant = name == COMPLIANT
    return [
        ("[Content_Types].xml", CONTENT_TYPES),
        ("_rels/.rels", ROOT_RELS),
        ("word/document.xml", _document_xml(compliant=compliant)),
        ("word/settings.xml", _settings_xml(compliant=compliant)),
        ("word/footnotes.xml", _footnotes_xml(compliant=compliant)),
        ("word/_rels/document.xml.rels", DOCUMENT_RELS),
        ("word/styles.xml", STYLES),
    ]


def build_bytes(name: str) -> bytes:
    if name not in SAMPLES:
        raise KeyError(f"未知样本：{name}")
    buf = BytesIO()
    with ZipFile(buf, "w", ZIP_DEFLATED) as z:
        for entry_name, xml in _parts(name):
            info = ZipInfo(entry_name, date_time=FIXED_TIME)
            info.compress_type = ZIP_DEFLATED
            z.writestr(info, xml.encode("utf-8"))
    return buf.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只比对，不写盘")
    parser.add_argument("--manifest", action="store_true", help="重算并打印 sha256")
    args = parser.parse_args()

    problems = 0
    digests = {}
    for name in SAMPLES:
        data = build_bytes(name)
        digests[name] = hashlib.sha256(data).hexdigest().upper()
        target = HERE / name
        if args.check:
            if not target.exists():
                print(f"缺失：{name}")
                problems += 1
            elif target.read_bytes() != data:
                print(f"与生成结果不一致：{name}")
                problems += 1
        else:
            target.write_bytes(data)
            print(f"已生成：{name}（{len(data)} 字节）")

    if args.manifest:
        print(json.dumps(digests, ensure_ascii=False, indent=2))

    if args.check:
        print("校验通过" if problems == 0 else f"校验失败：{problems} 个文件")
        return 1 if problems else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
