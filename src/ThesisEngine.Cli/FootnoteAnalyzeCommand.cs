using System.Text.Encodings.Web;
using System.Text.Json;
using ThesisEngine.Core;

namespace ThesisEngine.Cli;

/// <summary>
/// 注释体例检查命令（analyze-footnotes）。独立入口，只查不改。
/// 与 analyze 主命令互不影响：主命令仍把 footnotes 列为 unsupported scope。
/// </summary>
public sealed class FootnoteAnalyzeCommand
{
    private readonly FootnoteAnalyzer _analyzer;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public FootnoteAnalyzeCommand(
        FootnoteAnalyzer? analyzer = null,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        _analyzer = analyzer ?? new FootnoteAnalyzer();
        _output = output ?? Console.Out;
        _error = error ?? Console.Error;
    }

    public int Execute(string[] args)
    {
        string? path = null;
        string styleName = FootnoteStyleProfile.NankaiUndergraduate.Name;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" when i + 1 < args.Length:
                    path = args[++i];
                    break;
                case "--style" when i + 1 < args.Length:
                    styleName = args[++i];
                    break;
                case "--json":
                    json = true;
                    break;
                case "--help" or "-h":
                    WriteUsage();
                    return 0;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        _error.WriteLine($"未知参数：{args[i]}");
                        WriteUsage();
                        return 2;
                    }
                    path ??= args[i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            WriteUsage();
            return 2;
        }

        if (!FootnoteStyleProfile.TryResolve(styleName, out var profile))
        {
            _error.WriteLine($"未知口径：{styleName}。可用：{string.Join(" / ", FootnoteStyleProfile.AvailableNames)}");
            return 2;
        }

        if (!File.Exists(path))
        {
            _error.WriteLine($"找不到文件：{path}");
            return 1;
        }

        try
        {
            var result = _analyzer.Analyze(path, profile);
            if (json)
            {
                // 本地 CLI 输出到终端/管道，不是 HTML 上下文；放宽转义让中文可读。
                _output.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }));
            }
            else
            {
                WriteReport(result);
            }
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error.WriteLine($"分析失败：{ex.Message}");
            return 1;
        }
    }

    private void WriteReport(FootnoteAnalysisResult result)
    {
        _output.WriteLine("=== 注释体例检查报告 ===");
        _output.WriteLine();
        _output.WriteLine($"口径：{result.StyleName}（{result.StyleBasis}）");
        _output.WriteLine($"正文脚注引用：{result.FootnoteReferenceCount} 处；脚注条目：{result.FootnoteCount} 条");
        _output.WriteLine();

        _output.WriteLine($"问题：{result.Issues.Count} 条");
        foreach (var issue in result.Issues)
        {
            var where = issue.ParagraphIndex.HasValue ? $"第 {issue.ParagraphIndex} 段" : "文档级";
            _output.WriteLine($"  - [{issue.Code}] {where}：{issue.Message}");
        }
        _output.WriteLine();

        _output.WriteLine($"未检查 / 无法判定：{result.NotChecked.Count} 项");
        foreach (var item in result.NotChecked)
        {
            _output.WriteLine($"  - {item}");
        }

        if (result.MissingParts.Count > 0)
        {
            _output.WriteLine();
            _output.WriteLine($"缺少部件：{string.Join("、", result.MissingParts)}");
            _output.WriteLine("（缺部件不等于合规，相关判据已列入未检查项）");
        }
    }

    private void WriteUsage()
    {
        _output.WriteLine("用法：ThesisEngine.Cli analyze-footnotes <file.docx> [--style <口径>] [--json]");
        _output.WriteLine($"口径：{string.Join(" / ", FootnoteStyleProfile.AvailableNames)}（默认 nankai-undergrad）");
        _output.WriteLine("只查不改；序号形态以 w:footnotePr/w:numFmt 为准；OOXML 无分页信息，无法定位到页。");
    }
}
