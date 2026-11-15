using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ThesisEngine.Core;
using Xunit;

/// <summary>
/// Issue #153 注释体例检查。样本预期见
/// tests/fixtures/synthetic/citation-footnotes/README.md。
/// </summary>
public sealed class FootnoteAnalyzerTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Fixture(string name) =>
        Path.Combine(RepositoryRoot(), "tests", "fixtures", "synthetic", "citation-footnotes", name);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NkuThesisTool.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void CompliantSampleHasNoIssuesAndDeclaresWhatWasNotChecked()
    {
        var result = new FootnoteAnalyzer().Analyze(Fixture("footnote_compliant.docx"));

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.FootnoteReferenceCount);
        Assert.Equal(1, result.FootnoteCount);
        Assert.Empty(result.MissingParts);
        Assert.Contains(result.NotChecked, s => s.Contains("页码定位"));
    }

    [Fact]
    public void NonCompliantSampleReportsExactlyFourIssuesMatchingManualCount()
    {
        var result = new FootnoteAnalyzer().Analyze(Fixture("footnote_noncompliant.docx"));

        Assert.Equal(4, result.Issues.Count);
        Assert.Equal(
            new[] { "InlineNote", "NumberFormatMismatch", "RestartUndetermined", "MixedWithCitation" },
            result.Issues.Select(i => i.Code).ToArray());

        Assert.Equal(2, result.Issues.Single(i => i.Code == "InlineNote").ParagraphIndex);
        Assert.Equal(1, result.Issues.Single(i => i.Code == "MixedWithCitation").ParagraphIndex);
        Assert.Contains(result.NotChecked, s => s.Contains("w:numRestart"));
    }

    [Fact]
    public void InlineNoteIsDetectedButOrdinaryParenthesesAreNot()
    {
        var path = WriteMinimalDocx(
            "<w:p><w:r><w:t>普通括号（说明：这里不是注释）。</w:t></w:r></w:p>"
            + "<w:p><w:r><w:t>这里写了（注：补充说明）。</w:t></w:r></w:p>",
            CompliantSettings(),
            CompliantFootnotes());

        var result = new FootnoteAnalyzer().Analyze(path);

        var inline = Assert.Single(result.Issues, i => i.Code == "InlineNote");
        Assert.Equal(2, inline.ParagraphIndex);
    }

    [Fact]
    public void CsuProfileDoesNotEnforceNumberFormatOrRestart()
    {
        var result = new FootnoteAnalyzer().Analyze(
            Fixture("footnote_noncompliant.docx"),
            FootnoteStyleProfile.CsuGraduate);

        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(new[] { "InlineNote", "MixedWithCitation" },
            result.Issues.Select(i => i.Code).OrderBy(c => c).ToArray());
        Assert.Contains(result.NotChecked, s => s.Contains("序号形态"));
        Assert.Contains(result.NotChecked, s => s.Contains("每页重编号"));
    }

    [Fact]
    public void MissingNumberFormatIsReportedAsUndeterminedNotCompliant()
    {
        var path = WriteMinimalDocx(
            "<w:p><w:r><w:t>正文段落。</w:t></w:r></w:p>",
            $"<w:settings xmlns:w=\"{W}\"/>",
            CompliantFootnotes());

        var result = new FootnoteAnalyzer().Analyze(path);

        Assert.Contains(result.Issues, i => i.Code == "NumberFormatUndetermined");
        Assert.Contains(result.Issues, i => i.Code == "RestartUndetermined");
        Assert.Contains(result.NotChecked, s => s.Contains("缺 w:numFmt"));
        Assert.Contains(result.NotChecked, s => s.Contains("缺 w:numRestart"));
        Assert.Empty(result.MissingParts);
    }

    [Fact]
    public void CustomProfileCanExpressADifferentSchoolsValues()
    {
        var profile = FootnoteStyleProfile.Custom(
            "another-school",
            new[] { "decimal" },
            "continuous",
            "自定义口径：阿拉伯数字、连续编号");

        var result = new FootnoteAnalyzer().Analyze(Fixture("footnote_compliant.docx"), profile);

        Assert.DoesNotContain(result.Issues, i => i.Code == "NumberFormatUndetermined");
        Assert.Contains(result.Issues, i => i.Code == "NumberFormatMismatch");
        Assert.Contains(result.Issues, i => i.Code == "RestartMismatch");
    }

    [Fact]
    public void CliJsonIsMachineReadableAndKeepsStderrEmpty()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var command = NewCommand(output, error);

        var code = Invoke(command, new[] { Fixture("footnote_compliant.docx"), "--json" });

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("nankai-undergrad", json.RootElement.GetProperty("StyleName").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("Issues").GetArrayLength());
    }

    [Fact]
    public void CliRejectsUnknownStyleAndMissingFile()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var command = NewCommand(output, error);

        Assert.Equal(2, Invoke(command, new[] { Fixture("footnote_compliant.docx"), "--style", "nope" }));
        Assert.Equal(1, Invoke(command, new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx") }));
    }

    // CLI 项目在测试 csproj 里是 ReferenceOutputAssembly=false，沿用仓库既有的
    // 反射加载写法（见 CliRuleAnalysisTests），不去改工程配置。
    private static object NewCommand(TextWriter output, TextWriter error)
    {
        var root = RepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var path = Path.Combine(root, "src", "ThesisEngine.Cli", "bin", configuration, "net10.0", "ThesisEngine.Cli.dll");
        Assert.True(File.Exists(path), $"CLI 未构建：{path}");
        var type = Assembly.LoadFrom(path).GetType("ThesisEngine.Cli.FootnoteAnalyzeCommand")!;
        return Activator.CreateInstance(type, new object[] { new FootnoteAnalyzer(), output, error })!;
    }

    private static int Invoke(object command, string[] args) =>
        (int)command.GetType().GetMethod("Execute", new[] { typeof(string[]) })!
            .Invoke(command, new object[] { args })!;

    [Fact]
    public void FixtureDeclarationMatchesGeneratorAndDisk()
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Fixture("manifest.json")));
        var files = manifest.RootElement.GetProperty("samples")
            .EnumerateArray().Select(s => s.GetProperty("file").GetString()).OrderBy(f => f).ToArray();
        var disk = Directory.GetFiles(Path.GetDirectoryName(Fixture("manifest.json"))!, "*.docx")
            .Select(Path.GetFileName).OrderBy(f => f).ToArray();

        Assert.Equal(files, disk);
    }

    private static string CompliantSettings() =>
        $"<w:settings xmlns:w=\"{W}\"><w:footnotePr>"
        + "<w:numFmt w:val=\"decimalEnclosedCircle\"/><w:numRestart w:val=\"eachPage\"/>"
        + "</w:footnotePr></w:settings>";

    private static string CompliantFootnotes() =>
        $"<w:footnotes xmlns:w=\"{W}\">"
        + "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>"
        + "<w:footnote w:id=\"2\"><w:p><w:r><w:footnoteRef/></w:r>"
        + "<w:r><w:t>① 概念说明。</w:t></w:r></w:p></w:footnote>"
        + "</w:footnotes>";

    private static string WriteMinimalDocx(string bodyXml, string? settingsXml, string? footnotesXml)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        void Add(string name, string xml)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
        Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"/>");
        Add("word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body>{bodyXml}</w:body></w:document>");
        if (settingsXml != null) Add("word/settings.xml", settingsXml);
        if (footnotesXml != null) Add("word/footnotes.xml", footnotesXml);
        return path;
    }
}
