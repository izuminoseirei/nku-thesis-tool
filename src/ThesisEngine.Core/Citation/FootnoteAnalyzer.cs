using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ThesisEngine.Core;

/// <summary>一条注释体例问题。ParagraphIndex 为 1 基段落序号；与段落无关的项留空。</summary>
public sealed record FootnoteIssue(
    string Code,
    int? ParagraphIndex,
    string Message,
    string? PageHint = null);

/// <summary>
/// 分析结果。NotChecked 单独列出「压根没查 / 无法判定」的项，避免把「没查」
/// 混进「查了没问题」——报告里必须能分开看。
/// </summary>
public sealed record FootnoteAnalysisResult(
    string StyleName,
    string StyleBasis,
    int FootnoteReferenceCount,
    int FootnoteCount,
    IReadOnlyList<FootnoteIssue> Issues,
    IReadOnlyList<string> NotChecked,
    IReadOnlyList<string> MissingParts);

/// <summary>
/// 注释体例分析器：只查不改，读 document.xml / settings.xml / footnotes.xml。
///
/// 边界（写清楚，免得被当成「已经支持脚注」）：
/// - 这是独立入口，不改 WordFormattingReader，analyze 主命令仍把 footnotes 列为 unsupported scope。
/// - OOXML 没有渲染后的分页信息，因此所有问题只能定位到段落，不能定位到页。
/// - 序号形态以 w:footnotePr/w:numFmt 为准；脚注正文里手写的序号不重复判。
/// - 不判断「该不该加注释」，也不判注释里的著录格式（那是引用体例，属 #152）。
/// </summary>
public sealed class FootnoteAnalyzer
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W = MainNamespace;
    private static readonly Regex BracketCitation = new(@"\[\d{1,3}\]", RegexOptions.Compiled);

    private static readonly string[] InlineNoteMarkers =
        { "（注", "(注", "〔注", "[注", "（注释", "(注释" };

    public FootnoteAnalysisResult Analyze(string docxPath, FootnoteStyleProfile? profile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        profile ??= FootnoteStyleProfile.NankaiUndergraduate;

        using var stream = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var document = ReadPart(archive, "word/document.xml")
            ?? throw new InvalidDataException("文档中没有 document.xml");
        var settings = ReadPart(archive, "word/settings.xml");
        var footnotes = ReadPart(archive, "word/footnotes.xml");

        var missingParts = new List<string>();
        if (settings == null) missingParts.Add("word/settings.xml");
        if (footnotes == null) missingParts.Add("word/footnotes.xml");

        var body = document.Root?.Element(W + "body")
            ?? throw new InvalidDataException("文档中没有 body");
        var paragraphs = body.Descendants(W + "p").ToList();
        var texts = paragraphs
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)).Trim())
            .ToList();
        var referenceCount = body.Descendants(W + "footnoteReference").Count();

        // 文档级设置在 settings.xml，节级可覆盖；节级优先，但字段逐个取第一个非空值。
        var sectionProps = body.Descendants(W + "sectPr")
            .SelectMany(s => s.Elements(W + "footnotePr"))
            .Reverse()
            .ToList();
        var allProps = sectionProps
            .Concat(settings?.Root?.Elements(W + "footnotePr") ?? Enumerable.Empty<XElement>())
            .ToList();
        var numberFormat = FirstAttribute(allProps, "numFmt");
        var numberRestart = FirstAttribute(allProps, "numRestart");

        var noteTexts = ReadNoteTexts(footnotes);

        var issues = new List<FootnoteIssue>();
        var notChecked = new List<string>
        {
            "页码定位：OOXML 不含渲染后的分页信息，只能定位到段落，页号需 Word 实机确认"
        };

        // 1. 随文注：D3 要求注释一律页下注，不得写在正文括号里。
        for (var i = 0; i < texts.Count; i++)
        {
            if (!InlineNoteMarkers.Any(m => texts[i].Contains(m, StringComparison.Ordinal))) continue;
            issues.Add(new FootnoteIssue(
                "InlineNote",
                i + 1,
                $"第 {i + 1} 段出现随文注标记，注释应改为页下注（w:footnoteReference）"));
        }

        if (referenceCount == 0 && footnotes == null)
        {
            notChecked.Add("是否使用页下注：全文无脚注引用且无 footnotes.xml，本条不判断该不该加注释");
        }

        // 2. 序号形态
        if (profile.ExpectedNumberFormats.Count == 0)
        {
            notChecked.Add("序号形态：当前口径不限定（按学科惯例择一），未比对");
        }
        else if (string.IsNullOrWhiteSpace(numberFormat))
        {
            issues.Add(new FootnoteIssue(
                "NumberFormatUndetermined",
                null,
                $"w:footnotePr 里没有 w:numFmt，无法判定序号形态；期望 {string.Join(" / ", profile.ExpectedNumberFormats)}"));
            notChecked.Add("序号形态：XML 缺 w:numFmt，未比对，不按合规处理");
        }
        else if (!profile.ExpectedNumberFormats.Contains(numberFormat, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new FootnoteIssue(
                "NumberFormatMismatch",
                null,
                $"序号形态为 {numberFormat}，与当前口径期望的 {string.Join(" / ", profile.ExpectedNumberFormats)} 不符"));
        }

        // 3. 每页重新编号
        if (profile.ExpectedNumberRestart == null)
        {
            notChecked.Add("每页重编号：当前口径不限定，未比对");
        }
        else if (string.IsNullOrWhiteSpace(numberRestart))
        {
            issues.Add(new FootnoteIssue(
                "RestartUndetermined",
                null,
                $"w:footnotePr 里没有 w:numRestart，无法判定是否每页重编号；期望 {profile.ExpectedNumberRestart}"));
            notChecked.Add("每页重编号：XML 缺 w:numRestart，未比对，不按合规处理");
        }
        else if (!string.Equals(numberRestart, profile.ExpectedNumberRestart, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new FootnoteIssue(
                "RestartMismatch",
                null,
                $"重编号方式为 {numberRestart}，与当前口径期望的 {profile.ExpectedNumberRestart} 不符"));
        }

        // 4. 与正文引用编号混用：脚注正文里出现 [n]，且正文里也存在 [n]。
        // 只在两边同时出现时报，避免把「脚注里引用了一篇文献」一律当错。
        var firstBodyCitation = texts.FindIndex(t => BracketCitation.IsMatch(t));
        var noteWithCitation = noteTexts.FirstOrDefault(t => BracketCitation.IsMatch(t));
        if (firstBodyCitation >= 0 && noteWithCitation != null)
        {
            var marks = string.Join("、", BracketCitation.Matches(noteWithCitation)
                .Select(m => m.Value).Distinct().Take(3));
            issues.Add(new FootnoteIssue(
                "MixedWithCitation",
                firstBodyCitation + 1,
                $"脚注里出现 {marks} 形式的参考文献编号，与第 {firstBodyCitation + 1} 段的正文引用编号形态混用"));
        }
        else if (referenceCount == 0)
        {
            notChecked.Add("与正文引用编号混用：全文无脚注引用，未比对");
        }

        return new FootnoteAnalysisResult(
            profile.Name,
            profile.Basis,
            referenceCount,
            noteTexts.Count,
            issues,
            notChecked,
            missingParts);
    }

    private static XDocument? ReadPart(ZipArchive archive, string partName)
    {
        var entry = archive.GetEntry(partName);
        if (entry == null) return null;
        using var reader = new StreamReader(entry.Open());
        return XDocument.Load(reader);
    }

    private static string? FirstAttribute(IEnumerable<XElement> props, string localName) =>
        props.Select(p => p.Element(W + localName)?.Attribute(W + "val")?.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static List<string> ReadNoteTexts(XDocument? footnotes)
    {
        var result = new List<string>();
        if (footnotes?.Root == null) return result;
        foreach (var note in footnotes.Root.Elements(W + "footnote"))
        {
            var type = note.Attribute(W + "type")?.Value;
            if (string.Equals(type, "separator", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "continuationSeparator", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(string.Concat(note.Descendants(W + "t").Select(t => t.Value)).Trim());
        }
        return result;
    }
}
