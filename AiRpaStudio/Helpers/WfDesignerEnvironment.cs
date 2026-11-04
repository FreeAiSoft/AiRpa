using System;
using System.Activities.Core.Presentation;
using System.Activities.Presentation;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Validation;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.CSharp.Activities;
using RPA.Core;
using RPA.CSharpExpressionEditor;

namespace RPA.Helpers
{
    /// <summary>
    /// Среда rehosted-дизайнера WF: обязательные установки, без которых валидный XAML открывается
    /// «пустой» диаграммой или модальным окном «Конструктор рабочих процессов».
    /// <para>
    /// Один источник для всех путей открытия: студия (<see cref="LoadActivityToDesignerHelper"/>),
    /// тестовый стенд конструктора (<c>AiRpaStudio.Tests\Design\DesignerTestHost</c>) и будущие
    /// MCP-инструменты генерации. Дублировать эти установки нельзя: расхождение путей даёт
    /// «работает в одном инструменте, ломается в другом».
    /// </para>
    /// <para>
    /// Перечень требований (см. <c>docs\Valid_Xaml_Designer_Plan.md</c>, п. 2.3):
    /// <list type="bullet">
    /// <item><c>DesignerConfigurationService</c>: .NET Framework 4.5 + загрузка из недоверенного источника;</item>
    /// <item>метаданные дизайнера (<see cref="DesignerMetadata"/>) — иначе у стандартных активностей нет вьюх;</item>
    /// <item>Roslyn-редактор выражений C# (<see cref="ExpressionTextBox.RegisterExpressionActivityEditor"/>)
    /// — без него выражения в конструкторе не редактируются;</item>
    /// <item><see cref="IExpressionEditorService"/> — редактор выражений внутри инспектора свойств;</item>
    /// <item><see cref="IValidationErrorService"/> — иначе WF показывает модальное окно с ошибками.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class WfDesignerEnvironment
    {
        private static int _expressionEditorRegistered;
        private static int _designerMetadataRegistered;

        /// <summary>
        /// Настраивает дизайнер перед загрузкой документа. Идемпотентна: повторный вызов безопасен.
        /// </summary>
        /// <param name="designer">Созданный, но ещё не загруженный дизайнер.</param>
        /// <param name="validationErrorService">
        /// Сервис показа ошибок валидации. По умолчанию — студийный
        /// <see cref="DebugValidationErrorService"/> (пишет в <c>Debug</c>); стенд конструктора передаёт
        /// записывающую реализацию, чтобы ошибки доходили до теста.
        /// </param>
        internal static void Configure(WorkflowDesigner designer, IValidationErrorService validationErrorService = null)
        {
            if (designer == null)
            {
                throw new ArgumentNullException(nameof(designer));
            }

            RegisterExpressionEditor();

            DesignerConfigurationService configuration = designer.Context.Services.GetService<DesignerConfigurationService>();
            if (configuration != null)
            {
                configuration.TargetFrameworkName = new FrameworkName(".NETFramework", new Version(4, 5));
                configuration.LoadingFromUntrustedSourceEnabled = true;
            }

            designer.Context.Services.Publish<IExpressionEditorService>(new RoslynExpressionEditorService());
            designer.Context.Services.Publish(validationErrorService ?? (IValidationErrorService)new DebugValidationErrorService());
        }

        /// <summary>
        /// Регистрирует метаданные дизайнера для стандартных активностей WF. Регистрация статическая и
        /// общепроцессная, поэтому выполняется ровно один раз.
        /// <para>
        /// Живёт здесь, а не в тестовом стенде: сборки WF-презентации — деталь среды дизайнера,
        /// у студии и у стенда должна быть одна и та же точка входа.
        /// </para>
        /// </summary>
        internal static void RegisterDesignerMetadata()
        {
            if (Interlocked.Exchange(ref _designerMetadataRegistered, 1) == 1)
            {
                return;
            }

            new DesignerMetadata().Register();
        }

        /// <summary>
        /// Регистрирует Roslyn-редактор выражений для языка «C#». Регистрация статическая и
        /// общепроцессная, поэтому выполняется ровно один раз.
        /// </summary>
        internal static void RegisterExpressionEditor()
        {
            if (Interlocked.Exchange(ref _expressionEditorRegistered, 1) == 1)
            {
                return;
            }

            ExpressionTextBox.RegisterExpressionActivityEditor(
                new CSharpValue<string>().Language,
                typeof(RoslynExpressionEditor),
                CSharpExpressionHelper.CreateExpressionFromString);
        }

        /// <summary>
        /// Задаёт набор сборок, доступных выражениям дизайнера: сборка проекта (локальная) и сборки,
        /// перечисленные в <c>TextExpression.ReferencesForImplementation</c>.
        /// <para>
        /// Без <see cref="AssemblyContextControlItem"/> панель «Пространства имён» и Roslyn-редактор
        /// выражений не видят типов проекта, и корректный XAML выглядит «нерезолвленным».
        /// </para>
        /// </summary>
        /// <param name="designer">Дизайнер, в который загружен документ.</param>
        /// <param name="assemblyNames">Имена сборок; пустые строки и дубликаты отбрасываются.</param>
        /// <param name="localAssemblyName">Сборка проекта (то, что в XAML стоит в <c>AssemblyReference</c>).</param>
        internal static void SetAssemblyReferences(
            WorkflowDesigner designer,
            IEnumerable<string> assemblyNames,
            string localAssemblyName = null)
        {
            if (designer == null)
            {
                throw new ArgumentNullException(nameof(designer));
            }

            List<AssemblyName> references = ToAssemblyNames(assemblyNames);
            AssemblyName local = ToAssemblyName(localAssemblyName);

            if (local != null && !references.Any(reference => IsSameAssembly(reference, local)))
            {
                references.Add(local);
            }

            designer.Context.Items.SetValue(new AssemblyContextControlItem
            {
                ReferencedAssemblyNames = references,
                LocalAssemblyName = local
            });
        }

        /// <summary>
        /// Разбирает имена <c>AssemblyReference</c>. Неразборчивое имя пропускается: одна «мусорная»
        /// строка не должна мешать открытию остальных сборок.
        /// </summary>
        private static List<AssemblyName> ToAssemblyNames(IEnumerable<string> assemblyNames)
        {
            var result = new List<AssemblyName>();
            if (assemblyNames == null)
            {
                return result;
            }

            foreach (string name in assemblyNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                AssemblyName assemblyName = ToAssemblyName(name);
                if (assemblyName != null && !result.Any(existing => IsSameAssembly(existing, assemblyName)))
                {
                    result.Add(assemblyName);
                }
            }

            return result;
        }

        private static AssemblyName ToAssemblyName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            try
            {
                return new AssemblyName(name.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.FileLoadException)
            {
                return null;
            }
        }

        private static bool IsSameAssembly(AssemblyName left, AssemblyName right)
        {
            return string.Equals(left?.Name, right?.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
