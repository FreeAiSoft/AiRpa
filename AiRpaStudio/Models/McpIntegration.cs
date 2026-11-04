using System;
using RPA.Core;
using RPA.Core.Generation;
using RPA.Helpers;
using RpaCommon;

namespace RPA.Models
{
    public class McpIntegration
    {
        public static CallResult AddNewActivityToProject()
        {
            try
            {
                ActivityCreator activityCreator = new ActivityCreator();
                activityCreator.Create();
                if (activityCreator.IsCreate)
                {
                    // MCP-вызов приходит из фонового потока: открытие вкладки обязано уйти на UI-поток
                    // через DesignerOpenService, иначе WorkflowDesigner создаётся не в том потоке
                    // (исключения WPF и «пустая» диаграмма), а второй конструктор на тот же файл —
                    // дубликат вкладки. Ошибка открытия возвращается как результат инструмента.
                    DesignerOpenResult openResult = DesignerOpenService
                        .ForStudio(message => CustomWfDesigner.GetInstance().Log(message))
                        .OpenOrActivate(activityCreator.FilePath);

                    if (!openResult.Ok)
                    {
                        return new CallResult { Error = new Error(string.Join(Environment.NewLine, openResult.Errors)) };
                    }

                    ProjectManager.GetInstance().AddFile(activityCreator.FilePath);
                }
                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ErrorText.Of(ex)) };
            }
        }
    }
}
