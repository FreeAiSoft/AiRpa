using System.Collections.Generic;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Аргумент вложенной XAML-активности — то, что видит дизайнер без компиляции проекта:
    /// имя и тип из <c>x:Members/x:Property</c>.
    /// </summary>
    internal sealed class XamlTypeFacadeArgument
    {
        public string Name { get; set; }

        public string Type { get; set; }
    }

    /// <summary>
    /// Лёгкий «тип» вложенной XAML-активности: полное имя (<c>x:Class</c>), namespace, короткое имя
    /// и аргументы. Это и есть тот минимум (тип + namespace + аргументы), который нужен дизайнеру,
    /// чтобы нарисовать блок <c>SubProcessN</c> без сборки и без компиляции проекта.
    /// </summary>
    internal sealed class XamlTypeFacade
    {
        public string FullName { get; set; }

        public string Namespace { get; set; }

        public string Name { get; set; }

        public List<XamlTypeFacadeArgument> Arguments { get; } = new List<XamlTypeFacadeArgument>();
    }
}
