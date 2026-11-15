using ThesisEngine.Core;

namespace ThesisEngine.Cli;

/// <summary>
/// 论文模板体检：读学校官方模板的 styles.xml，报告关键角色落在哪条样式上、还有哪些样式没识别。
/// 只读，不改文件。
/// </summary>
public sealed class AnalyzeTemplateCommand
{
    private readonly TemplateInspector _inspector;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public AnalyzeTemplateCommand(
        TemplateInspector? inspector = null,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        _inspector = inspector ?? new TemplateInspector();
        _output = output ?? Console.Out;
        _error = error ?? Console.Error;
    }

    public int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            _error.WriteLine("用法：ThesisEngine.Cli analyze-template <模板.dotx|模板.docx>");
            return 2;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            _error.WriteLine($"找不到文件：{path}");
            return 1;
        }

        try
        {
            var result = _inspector.Inspect(path);

            _output.WriteLine("=== 论文模板样式报告 ===");
            _output.WriteLine();
            _output.WriteLine($"模板：{Path.GetFileName(path)}");
            _output.WriteLine($"样式总数：{result.StyleCount}（其中自定义 {result.CustomStyleCount}）");
            _output.WriteLine();

            _output.WriteLine("关键角色：");
            foreach (var role in result.Roles)
            {
                if (role.IsMatched)
                {
                    _output.WriteLine($"  ✅ {role.Description}\tstyleId=\"{role.StyleId}\"\tname=\"{role.StyleName}\"");
                }
                else
                {
                    _output.WriteLine($"  ❌ {role.Description}\t未找到");
                }
            }

            if (result.UnrecognizedStyles.Count > 0)
            {
                _output.WriteLine();
                _output.WriteLine($"未识别的自定义样式（{result.UnrecognizedStyles.Count} 条）：");
                foreach (var style in result.UnrecognizedStyles.Take(20))
                {
                    _output.WriteLine($"  - \"{style.Name}\"（styleId=\"{style.StyleId}\"，基于 {style.BasedOn ?? "无"}）");
                }

                if (result.UnrecognizedStyles.Count > 20)
                {
                    _output.WriteLine($"  ... 还有 {result.UnrecognizedStyles.Count - 20} 条");
                }
            }

            if (result.Notes.Count > 0)
            {
                _output.WriteLine();
                _output.WriteLine("提示：");
                foreach (var note in result.Notes)
                {
                    _output.WriteLine($"  - {note}");
                }
            }

            var missing = result.Roles.Count(role => !role.IsMatched);
            _output.WriteLine();
            _output.WriteLine(missing == 0
                ? $"结论：{result.Roles.Count} 个关键角色全部能在模板里找到，可以进入下一步（套用模板样式）。"
                : $"结论：{missing} 个关键角色在模板里找不到，套用前需要补齐或人工确认。");

            return 0;
        }
        catch (Exception ex)
        {
            _error.WriteLine($"失败：{ex.Message}");
            return 1;
        }
    }
}
