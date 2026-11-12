using System;
using System.Collections.Generic;
using System.IO;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Реестр вложенных XAML-активностей проекта: карта <c>x:Class → (фасад, исходный файл)</c>,
    /// построенная только чтением шапки каждого XAML (<see cref="XamlTypeFacadeBuilder"/>), без
    /// компиляции и без чтения тел workflow.
    /// <para>
    /// Один битый XML-файл не валит весь реестр: такой файл пропускается. Файл без <c>x:Class</c> тоже
    /// пропускается — по нему нельзя построить ссылку <c>clr-namespace</c>.
    /// </para>
    /// </summary>
    internal sealed class WorkflowTypeRegistry
    {
        private readonly Dictionary<string, XamlTypeFacade> _facades;
        private readonly Dictionary<string, string> _sourceFiles;

        private WorkflowTypeRegistry()
        {
            _facades = new Dictionary<string, XamlTypeFacade>(StringComparer.Ordinal);
            _sourceFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal int Count => _facades.Count;

        internal IEnumerable<string> FullNames => _facades.Keys;

        internal static WorkflowTypeRegistry Scan(IEnumerable<string> xamlFiles)
        {
            var registry = new WorkflowTypeRegistry();

            if (xamlFiles == null)
            {
                return registry;
            }

            foreach (string file in xamlFiles)
            {
                registry.Index(file);
            }

            return registry;
        }

        internal bool TryGet(string fullName, out XamlTypeFacade facade)
        {
            return _facades.TryGetValue(fullName ?? string.Empty, out facade);
        }

        internal bool TryGetSourceFile(string fullName, out string sourceFile)
        {
            return _sourceFiles.TryGetValue(fullName ?? string.Empty, out sourceFile);
        }

        private void Index(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            string fullPath = Path.GetFullPath(filePath);

            string xaml;
            try
            {
                xaml = File.ReadAllText(fullPath);
            }
            catch (Exception)
            {
                // Файл занят другим процессом или недоступен: не мешаем остальным файлам реестра.
                return;
            }

            XamlTypeFacade facade;
            try
            {
                facade = XamlTypeFacadeBuilder.Build(xaml);
            }
            catch (Exception)
            {
                // Не XML / битая шапка: такой файл не участвует в резолве ссылок.
                return;
            }

            if (string.IsNullOrWhiteSpace(facade.FullName))
            {
                return;
            }

            // Первый победил: дубликат x:Class — ошибка проекта, но реестр не должен падать.
            if (!_facades.ContainsKey(facade.FullName))
            {
                _facades.Add(facade.FullName, facade);
                _sourceFiles.Add(facade.FullName, fullPath);
            }
        }
    }
}
