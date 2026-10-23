using RPA.Core;
using RPA.Models;

namespace RPA
{
    internal static class Services
    {
        public static AppSettings AppSettings => AppSettings.GetInstance();
        public static ProjectManager ProjectManager => ProjectManager.GetInstance();
        public static CustomWfDesigner CustomWfDesigner => CustomWfDesigner.GetInstance();
    }
}
