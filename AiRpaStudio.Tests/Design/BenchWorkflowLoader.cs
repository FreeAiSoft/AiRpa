using System.IO;
using RPA.Core;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Открытие XAML в дизайнере стенда.
/// <para>
/// Проектный файл с вложенными <c>clr-namespace</c>-ссылками (например <c>RPA-1\Main.xaml</c> ссылается
/// на <c>&lt;local:SubProcess1 /&gt;</c>) открывается loose-путём через <see cref="LooseXamlDesignerLoader"/> —
/// так же, как студия. Одиночный/временный файл без <c>x:Class</c>-реестра открывается штатным
/// <c>WorkflowDesigner.Load</c>: в этом режиме WF подставляет <c>ErrorActivity</c> для неизвестного типа,
/// и негативные сценарии стенда продолжают видеть «нераспознанную активность».
/// </para>
/// </summary>
internal static class BenchWorkflowLoader
{
    internal static void Open(WorkflowDesignerEx designer, string xamlPath)
    {
        string projectFile = FindProjectFile(xamlPath);
        if (projectFile == null)
        {
            designer.Load(xamlPath);
            return;
        }

        WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(
            Directory.GetFiles(Path.GetDirectoryName(projectFile), "*.xaml"));

        if (registry.Count > 0)
        {
            LooseXamlDesignerLoader.Open(designer, xamlPath, registry);
        }
        else
        {
            designer.Load(xamlPath);
        }
    }
    private static string FindProjectFile(string xamlPath)
    {
        string directory = Path.GetDirectoryName(xamlPath);
        while (!string.IsNullOrEmpty(directory))
        {
            string[] projects = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length > 0)
            {
                return projects[0];
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

}
