using System.IO.Compression;
using System.Xml.Linq;

namespace ThesisEngine.Core;

/// <summary>模板里的一条样式定义。</summary>
public sealed record TemplateStyle(
    string StyleId,
    string? Name,
    string? Type,
    string? BasedOn,
    bool IsCustom);

/// <summary>一个关键角色在模板里的落点。StyleId 为空表示模板里没有这个角色。</summary>
public sealed record TemplateRole(
    string Role,
    string Description,
    string? StyleId,
    string? StyleName)
{
    public bool IsMatched => StyleId != null;
}

/// <summary>
/// 论文模板体检结果：模板里有哪些样式、工具需要的角色各落在哪条样式上、还有哪些样式没被识别。
/// </summary>
public sealed record TemplateInspection(
    string TemplatePath,
    int StyleCount,
    int CustomStyleCount,
    IReadOnlyList<TemplateRole> Roles,
    IReadOnlyList<TemplateStyle> UnrecognizedStyles,
    IReadOnlyList<string> Notes);

/// <summary>
/// 读取学校官方模板（.dotx / .docx）里的 <c>word/styles.xml</c>，把样式按“角色”归位。
///
/// 这是「三步法」第 1 步的前置：先看清模板里到底有什么，再谈把模板样式灌进待排版文档。
/// 只读，不改任何文件。
/// </summary>
public sealed class TemplateInspector
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W = MainNamespace;

    /// <summary>工具关心的角色，按报告顺序排列。</summary>
    private static readonly (string Role, string Description, string[] Aliases)[] KnownRoles =
    {
        ("Heading1", "一级标题", new[] { "heading1", "标题1", "1级标题" }),
        ("Heading2", "二级标题", new[] { "heading2", "标题2", "2级标题" }),
        ("Heading3", "三级标题", new[] { "heading3", "标题3", "3级标题" }),
        ("Body", "正文", new[] { "normal", "正文", "正文文本" }),
        ("Caption", "题注（图题/表题）", new[] { "caption", "题注", "图题", "表题", "图注", "表注" }),
        ("Bibliography", "参考文献条目", new[] { "bibliography", "references", "参考文献" }),
        ("Table", "表格", new[] { "tablegrid", "table", "表格", "三线表" })
    };

    public TemplateInspection Inspect(string templatePath)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"找不到模板文件：{templatePath}", templatePath);
        }

        var styles = ReadStyles(templatePath);
        var notes = new List<string>();

        var roles = KnownRoles
            .Select(role => MatchRole(role.Role, role.Description, role.Aliases, styles))
            .ToList();

        var matchedIds = roles
            .Where(role => role.IsMatched)
            .Select(role => role.StyleId!)
            .ToHashSet(StringComparer.Ordinal);

        var unrecognized = styles
            .Where(style => !matchedIds.Contains(style.StyleId))
            .Where(style => style.IsCustom)
            .Where(style => IsParagraphOrTableStyle(style.Type))
            .ToList();

        var characterStyleCount = styles.Count(style =>
            string.Equals(style.Type, "character", StringComparison.OrdinalIgnoreCase));

        if (styles.Count == 0)
        {
            notes.Add("模板里没有样式定义，Word 会全部按默认样式渲染");
        }

        if (roles.Any(role => role.Role == "Body" && !role.IsMatched))
        {
            notes.Add("没有找到「正文 / Normal」样式，套用模板后正文会退回 Word 默认字体");
        }

        if (roles.Any(role => role.Role == "Caption" && !role.IsMatched))
        {
            notes.Add("没有找到「题注 / Caption」样式，图表编号无法靠样式统一");
        }

        if (unrecognized.Count > 0)
        {
            notes.Add($"有 {unrecognized.Count} 条自定义段落/表格样式未对应到工具角色，可能是学校模板自己的命名，需要人工确认");
        }

        if (characterStyleCount > 0)
        {
            notes.Add($"另有 {characterStyleCount} 条字符样式（如“标题 1 字符”）不参与角色匹配，未列入未识别清单");
        }

        return new TemplateInspection(
            templatePath,
            styles.Count,
            styles.Count(style => style.IsCustom),
            roles,
            unrecognized,
            notes);
    }

    private static TemplateRole MatchRole(
        string role,
        string description,
        IReadOnlyList<string> aliases,
        IReadOnlyList<TemplateStyle> styles)
    {
        foreach (var style in styles)
        {
            var key = Normalize(style.Name ?? style.StyleId);
            if (aliases.Any(alias => string.Equals(key, Normalize(alias), StringComparison.Ordinal)))
            {
                return new TemplateRole(role, description, style.StyleId, style.Name);
            }
        }

        return new TemplateRole(role, description, null, null);
    }

    private static List<TemplateStyle> ReadStyles(string templatePath)
    {
        using var stream = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.GetEntry("word/styles.xml")
            ?? throw new InvalidDataException("模板里没有 word/styles.xml，不是有效的 Word 模板");

        XDocument document;
        using (var reader = new StreamReader(entry.Open()))
        {
            document = XDocument.Load(reader);
        }

        return document.Root?
            .Elements(W + "style")
            .Select(style => new TemplateStyle(
                StyleId: (string?)style.Attribute(W + "styleId") ?? "",
                Name: (string?)style.Element(W + "name")?.Attribute(W + "val"),
                Type: (string?)style.Attribute(W + "type"),
                BasedOn: (string?)style.Element(W + "basedOn")?.Attribute(W + "val"),
                IsCustom: string.Equals((string?)style.Attribute(W + "customStyle"), "1", StringComparison.Ordinal)))
            .Where(style => style.StyleId.Length > 0)
            .ToList()
            ?? new List<TemplateStyle>();
    }

    /// <summary>字符样式挂在 run 上，不是段落角色，不参与“未识别”清单。</summary>
    private static bool IsParagraphOrTableStyle(string? type) =>
        type == null
        || string.Equals(type, "paragraph", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "table", StringComparison.OrdinalIgnoreCase);

    /// <summary>去掉空白与全角空格，统一大小写，便于中英文样式名比较。</summary>
    private static string Normalize(string value) =>
        new string(value.Where(c => !char.IsWhiteSpace(c) && c != '\u3000').ToArray()).ToLowerInvariant();
}
