namespace ThesisEngine.Core;

/// <summary>
/// 注释体例口径：只描述「期望什么」，不把任何一所学校的取值写死进检查逻辑。
///
/// 取值来源见 docs/research/discipline-specs-2026-11-11.md 的 D2 / D3；
/// 具体规范值由负责人维护在 rules/，这里两个内置实例只是命令行快捷入口，
/// 新增培养单位用 Custom(...) 传入，或以后从 rules/ 装载。
/// </summary>
public sealed class FootnoteStyleProfile
{
    private FootnoteStyleProfile(
        string name,
        IReadOnlyList<string> expectedNumberFormats,
        string? expectedNumberRestart,
        string basis)
    {
        Name = name;
        ExpectedNumberFormats = expectedNumberFormats;
        ExpectedNumberRestart = expectedNumberRestart;
        Basis = basis;
    }

    public string Name { get; }

    /// <summary>w:numFmt/@w:val 的可接受值；空表示该口径不限定序号形态。</summary>
    public IReadOnlyList<string> ExpectedNumberFormats { get; }

    /// <summary>w:numRestart/@w:val 的期望值；null 表示该口径不限定重编号方式。</summary>
    public string? ExpectedNumberRestart { get; }

    /// <summary>口径依据，报告里原样输出，便于人工复核。</summary>
    public string Basis { get; }

    /// <summary>D3 南开本科手册：注释一律页下注，序号 ①②③，每页独立编号。</summary>
    public static FootnoteStyleProfile NankaiUndergraduate { get; } = new(
        "nankai-undergrad",
        new[] { "decimalEnclosedCircle", "decimalEnclosedCircleChinese", "ideographEnclosedCircle" },
        "eachPage",
        "D3 南开本科手册：注释一律采用页下注，序号 ①②③，每页独立编号");

    /// <summary>
    /// D2 中南大学研究生规范 §9：顺序编码制与页下注并存，作者按学科惯例择一。
    /// 因此不强制序号形态与重编号方式，只做「有没有随文注」「有没有混用」两项通用检查。
    /// </summary>
    public static FootnoteStyleProfile CsuGraduate { get; } = new(
        "csu-grad",
        Array.Empty<string>(),
        null,
        "D2 中南大学研究生规范 §9：按学科惯例择一，不强制序号形态与重编号方式");

    public static FootnoteStyleProfile Custom(
        string name,
        IReadOnlyList<string>? expectedNumberFormats,
        string? expectedNumberRestart,
        string basis)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("口径名不能为空", nameof(name));
        return new FootnoteStyleProfile(
            name,
            expectedNumberFormats ?? Array.Empty<string>(),
            expectedNumberRestart,
            string.IsNullOrWhiteSpace(basis) ? "自定义口径（未注明依据）" : basis);
    }

    public static IReadOnlyList<string> AvailableNames =>
        new[] { NankaiUndergraduate.Name, CsuGraduate.Name };

    public static bool TryResolve(string? name, out FootnoteStyleProfile profile)
    {
        if (string.Equals(name, NankaiUndergraduate.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "nankai", StringComparison.OrdinalIgnoreCase))
        {
            profile = NankaiUndergraduate;
            return true;
        }

        if (string.Equals(name, CsuGraduate.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "csu", StringComparison.OrdinalIgnoreCase))
        {
            profile = CsuGraduate;
            return true;
        }

        profile = NankaiUndergraduate;
        return false;
    }
}
