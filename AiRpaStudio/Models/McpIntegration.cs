using System;
using RPA.Core;
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
                    CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(activityCreator.FilePath);
                    ProjectManager.GetInstance().AddFile(activityCreator.FilePath);
                }
                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex.Message) };
            }
        }
    }
}
