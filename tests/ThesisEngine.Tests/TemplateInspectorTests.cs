using System.IO.Compression;
using System.Xml.Linq;
using ThesisEngine.Core;
using Xunit;

namespace ThesisEngine.Tests;

public sealed class TemplateInspectorTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void RoleStylesAreMatchedByChineseAndEnglishNames()
    {
        var path = CreateTemplate("""
            <w:style w:type="paragraph" w:styleId="1"><w:name w:val="heading 1"/></w:style>
            <w:style w:type="paragraph" w:styleId="2"><w:name w:val="heading 2"/></w:style>
            <w:style w:type="paragraph" w:styleId="3"><w:name w:val="heading 3"/></w:style>
            <w:style w:type="paragraph" w:styleId="a"><w:name w:val="Normal"/></w:style>
            <w:style w:type="paragraph" w:styleId="Caption"><w:name w:val="caption"/></w:style>
            <w:style w:type="paragraph" w:styleId="Bib"><w:name w:val="参考文献"/></w:style>
            <w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/></w:style>
            """);
        try
        {
            var result = new TemplateInspector().Inspect(path);

            Assert.Equal(7, result.StyleCount);
            Assert.All(result.Roles, role => Assert.True(role.IsMatched, $"{role.Role} 应该能匹配到样式"));
            Assert.Equal("1", result.Roles.Single(role => role.Role == "Heading1").StyleId);
            Assert.Equal("a", result.Roles.Single(role => role.Role == "Body").StyleId);
            Assert.Empty(result.Notes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingBodyAndCaptionProduceNotes()
    {
        var path = CreateTemplate("""
            <w:style w:type="paragraph" w:styleId="1"><w:name w:val="heading 1"/></w:style>
            """);
        try
        {
            var result = new TemplateInspector().Inspect(path);

            var body = result.Roles.Single(role => role.Role == "Body");
            var caption = result.Roles.Single(role => role.Role == "Caption");
            Assert.False(body.IsMatched);
            Assert.False(caption.IsMatched);
            Assert.Contains(result.Notes, note => note.Contains("正文"));
            Assert.Contains(result.Notes, note => note.Contains("题注"));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// 字符样式挂在 run 上，不是段落角色；这里最容易把“标题 1 字符”“页眉 字符”刷成一片误报。
    /// </summary>
    [Fact]
    public void CharacterStylesAreExcludedFromUnrecognizedList()
    {
        var path = CreateTemplate("""
            <w:style w:type="character" w:styleId="Heading1Char" w:customStyle="1"><w:name w:val="标题 1 字符"/></w:style>
            <w:style w:type="paragraph" w:styleId="NkuBody" w:customStyle="1"><w:name w:val="南开正文"/></w:style>
            """);
        try
        {
            var result = new TemplateInspector().Inspect(path);

            var unrecognized = Assert.Single(result.UnrecognizedStyles);
            Assert.Equal("NkuBody", unrecognized.StyleId);
            Assert.Contains(result.Notes, note => note.Contains("字符样式"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FileWithoutStylesPartIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dotx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(archive, "[Content_Types].xml",
                    """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>""");
            }

            var error = Assert.Throws<InvalidDataException>(() => new TemplateInspector().Inspect(path));
            Assert.Contains("styles.xml", error.Message);
        }
        finally { File.Delete(path); }
    }

    private static string CreateTemplate(string stylesXml)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dotx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "[Content_Types].xml",
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/></Types>""");
        Add(archive, "word/styles.xml",
            $"<w:styles xmlns:w=\"{W}\">{stylesXml}</w:styles>");
        return path;
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
