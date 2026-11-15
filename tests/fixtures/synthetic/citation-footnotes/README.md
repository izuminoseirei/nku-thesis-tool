# citation-footnotes/ — 注释体例检查合成样本

Issue #153。两份都是人工构造的最小 docx，只保留本条检查会读的三个部件：
`word/document.xml`（正文 + `sectPr/w:footnotePr`）、`word/settings.xml`（文档级 `footnotePr`）、
`word/footnotes.xml`（脚注正文）。另附最小 `styles.xml`、`_rels` 与 `[Content_Types].xml`，
让文件能被常规 docx 读取器打开。

## 依据

| 口径 | 原文要点 | 出处 |
|---|---|---|
| `nankai-undergrad` | 注释一律采用页下注（脚注），不是随文注或篇末注；序号 ①②③、每页独立编号 | D3 南开本科手册，摘录见 `docs/research/discipline-specs-2026-11-11.md` 第 42 行 |
| `csu-grad` | 顺序编码制与页下注并存，作者按学科惯例择一；人文社科可改用页下注 | D2 中南大学研究生规范 §9，摘录见同文件第 40–41 行 |

因此**序号形态与重编号方式不是全库统一值**：`FootnoteStyleProfile` 按口径区分，
`csu-grad` 不限定这两项、报告里进「未检查项」，不是「查了没问题」。
`rules/` 里的规范值由负责人补，代码不写死单校。

## 两份样本

| 样本 | 构造 | 南开口径预期 |
|---|---|---|
| `footnote_compliant.docx` | `w:numFmt=decimalEnclosedCircle`、`w:numRestart=eachPage`、注释走 `w:footnoteReference`、脚注正文不含 `[n]`、正文无随文注 | **0 条问题** |
| `footnote_noncompliant.docx` | 第 2 段写「（注：…）」；`w:numFmt=decimal`；缺 `w:numRestart`；脚注正文含 `[1]` 且正文第 1 段也有 `[1]` | **4 条**：`InlineNote`(第 2 段)、`NumberFormatMismatch`、`RestartUndetermined`、`MixedWithCitation`(第 1 段) |

条数与人工核对一致，断言写在 `tests/ThesisEngine.Tests/FootnoteAnalyzerTests.cs`。

## 判据边界（别当成「已经支持脚注」）

- 序号形态以 `w:footnotePr/w:numFmt` 为准；脚注正文里手写的 `①` 或 `1.` 不重复判。
- `w:numFmt` / `w:numRestart` 缺失时报 `…Undetermined`，**不按合规处理**，同时进「未检查项」。
- 「随文注」只匹配 `（注` / `(注` / `〔注` / `[注` / `（注释` / `(注释` 这类显式标记；
  不对圈号做全文扫描，否则正文列表符号会被误报。
- 「与正文引用编号混用」只在正文与脚注**同时**出现 `[n]` 时报；脚注里单纯引用一篇文献不算错。
- OOXML 没有渲染后的分页信息，**所有问题只能定位到段落，不能定位到页**；报告里列为未检查项，
  页号需 Word 实机确认。
- 不判断「该不该加注释」，不判注释里的著录格式（属 #152 引用体例）。
- 本检查是独立入口 `analyze-footnotes`，不改 `WordFormattingReader`；`analyze` 主命令仍把
  `footnotes` 列为 unsupported scope，两者不冲突，也不能据此宣称全库已支持脚注。

## 复现

```bash
cd tests/fixtures/synthetic/citation-footnotes
python generate.py --check    # 重建两份，与磁盘比整文件字节，不写盘
python generate.py            # 重新生成（覆盖）
python generate.py --manifest # 打印 sha256，用于回填 manifest.json
```

零依赖，只用标准库。ZIP 条目顺序与 `date_time` 固定，`--check` 比整文件字节。
清单三方一致由仓库根目录的 `verify_fixtures.py` 统一核对。
