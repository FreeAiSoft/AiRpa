using System;
using System.IO;
using RPA.Entities;
using RPA.Enums;
using RPA.VsProject;

namespace RPA.ProjectModel
{
    /// <summary>
    /// Маппинг единого доменного объекта <see cref="Project"/> ↔ DTO парсинга
    /// <see cref="VsProjectInfo"/>. VS-проект всегда является activity library, поэтому
    /// <see cref="ProjectType.Library"/> проставляется без эвристик.
    /// </summary>
    public static class ProjectMapper
    {
        public static Project FromVsProjectInfo(VsProjectInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            return new Project(ProjectType.Library)
            {
                Name = info.Name,
                MainActivity = FindMainActivity(info),
                Format = ProjectFormat.VsProject,
                ProjectFilePath = info.ProjectPath,
                RootNamespace = info.RootNamespace,
                AssemblyName = info.AssemblyName,
                OutputType = info.OutputType,
                TargetFrameworkVersion = info.TargetFrameworkVersion,
                TargetFramework = info.PrimaryTargetFramework,
                ProjectGuid = info.ProjectGuid,
                IsSdkStyle = info.IsSdkStyle,
                IsWorkflowProject = info.IsWorkflowProject,
                Packages = info.Packages,
                Items = info.Items
            };
        }

        /// <summary>
        /// MainActivity — предпочтительно <c>Main.xaml</c>, иначе первый <c>XamlAppDef</c>.
        /// Путь относительный (как в <c>Include</c> .csproj).
        /// </summary>
        private static string FindMainActivity(VsProjectInfo info)
        {
            VsProjectItem first = null;

            foreach (VsProjectItem item in info.Items)
            {
                if (!IsXamlItem(item))
                {
                    continue;
                }

                first = first ?? item;

                string fileName = Path.GetFileName(item.Include ?? string.Empty);
                if (string.Equals(fileName, Consts.defaultMainActivityFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Include;
                }
            }

            return first?.Include;
        }

        private static bool IsXamlItem(VsProjectItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (string.Equals(item.ItemType, "XamlAppDef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(item.Include)
                   && item.Include.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        }
    }
}
