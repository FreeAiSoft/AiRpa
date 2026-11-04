using System;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.get_workflow</c> — дерево процесса для агента (этап 4 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>): идентификаторы активностей, типы,
    /// подписи и заданные выражения аргументов.
    /// <para>
    /// Это источник идентификаторов для <c>xaml.add_activity</c> и <c>xaml.set_argument</c>:
    /// без него агент не может адресовать активность внутри конструктора.
    /// </para>
    /// </summary>
    internal sealed class GetWorkflowTool : PaletteToolBase
    {
        internal const string ToolName = "xaml.get_workflow";

        internal GetWorkflowTool(Func<IPaletteWorkflowService> serviceFactory)
            : base(serviceFactory)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Возвращает дерево процесса: активности с идентификаторами, типами, подписями и заданными " +
                    "выражениями аргументов. Идентификаторы нужны для xaml.add_activity и xaml.set_argument.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        includeXaml = new
                        {
                            type = "boolean",
                            description = "Вернуть текст XAML файла процесса (по умолчанию false)"
                        }
                    },
                    required = new[] { "path" }
                }
            };
        }

        protected override McpToolResult ExecuteCore(JObject arguments)
        {
            string path = Text(arguments, "path", "filePath");
            if (string.IsNullOrWhiteSpace(path))
            {
                return ParameterError("path", "xaml.path: путь к файлу процесса не задан", null, GetSchema());
            }

            var request = new PaletteGetWorkflowRequest
            {
                FilePath = path,
                IncludeXaml = Flag(arguments, "includeXaml")
            };

            return Respond(Service.GetWorkflow(request));
        }
    }
}
