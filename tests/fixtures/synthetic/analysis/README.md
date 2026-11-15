# analysis/ 规则分析回归样本

Issue #111 的规则分析回归样本，两份，均为合成数据，不含真实论文内容。

| 文件 | 内容 | 预期 |
| --- | --- | --- |
| `analysis_compliant.docx` | A4、上下 3.8 cm / 左右 3.2 cm 页边距、宋体 + Times New Roman 12 pt、两端对齐、首行缩进 2 字符、固定值 20 pt | `analyze` 0 条不符合，「未检查」清单非空 |
| `analysis_noncompliant.docx` | 同一基础文档，第一段改成黑体 / Arial 10.5 pt、左对齐、0 缩进、单倍行距、段前段后各 6 pt | 8 条不符合 |

「未检查非空」是这条样本的一半用途：0 条不符合只说明已实现的规则都过了，不说明整篇合规，所以还要看未覆盖清单里还剩什么。这两条都不构成正式合规结论。

## 生成与校验

```bash
cd tests/fixtures/synthetic/analysis
python generate.py --check    # 重建两份，与磁盘比整文件字节，不写盘
python generate.py            # 重建并写盘（覆盖本目录的已提交样本）
```

重建需要 `python-docx`（`pip install -r requirements.txt`）。但**清单核对不需要**：`generate.py` 顶层只导入标准库，`verify_fixtures.py` 读 `SAMPLES` 时不会碰 python-docx，所以干净检出下也能跑清单一致性那一条。

`--check` 比的是**整文件字节**，不是逐部件：`build_bytes()` 末尾固定了 ZIP 条目时间戳（`(2000, 1, 1, 0, 0, 0)`，与 `format-samples/`、`full-thesis/` 同一个常量），同一生成器在任何机器、任何时刻跑出的字节都相同。python-docx 的 `save()` 默认拿当前时刻当时间戳，不做这一步的话整文件哈希就成了「某一时刻的快照」。

## 与其它目录的关系

两组样本出自同一个 `base_document()`，不从磁盘读另一份。基础文档被改坏时两份会一起变形，逐部件重建能同时抓出来，不会出现「源改了、另一份还是旧的，两份都算过」。

原先这两份 docx 与生成脚本平铺在 `tests/fixtures/synthetic/` 根下，没有 `manifest.json`，`verify_fixtures.py` 的清单一致性扫不到它们。现在收进独立目录，与其它六组生成器同形：清单常量 `SAMPLES`、构建函数 `build_bytes(name)` 返回 docx 字节。

改写前后解压的 17 个部件一字未改，文件长度仍是 37254 / 37280 字节，`sha256` 变化只来自容器头部时间戳——旧的哈希记在 `manifest.json` 的 `regeneration.previousSha256` 里。

## 一个没做的检查

两份样本都只有一章、三段正文、一条参考文献，用来验证段落级规则。**图表、公式、页眉页脚、分节页码这些都不在里面**，它们由 `full-thesis/` 的合成论文覆盖。别拿这两份当「整篇论文都验过了」。
