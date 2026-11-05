using System;
using System.Collections.Generic;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Каталог активностей студии для агента (карточка <c>AG-01</c>): поиск и полное описание
    /// активностей по их реальным типам.
    /// <para>
    /// Реализацию предоставляет хост студии (у неё есть WF-типы и библиотеки активностей), поэтому
    /// чат зависит только от интерфейса, а инструменты <c>activity_search</c>/<c>activity_describe</c>
    /// тестируются без UI и без WF.
    /// </para>
    /// </summary>
    public interface IActivityCatalog
    {
        /// <summary>Число активностей в каталоге.</summary>
        int Count { get; }

        /// <summary>Читаемые имена категорий каталога в порядке палитры.</summary>
        IReadOnlyList<string> Categories { get; }

        /// <summary>
        /// Поиск активностей по подстроке (ключ, подпись, полное имя типа, описание, категория)
        /// и/или по категории. <paramref name="limit"/> &lt;= 0 — предел каталога по умолчанию.
        /// </summary>
        IReadOnlyList<ActivityInfo> Search(string query, string category = null, int limit = 0);

        /// <summary>
        /// Описание активности по имени: <c>Sequence</c>, <c>ForEach&lt;&gt;</c>, <c>ForEach`1</c>,
        /// <c>System.Activities.Statements.WriteLine</c>. <c>null</c>, если активности нет.
        /// </summary>
        ActivityInfo Describe(string typeName);
    }

    /// <summary>
    /// Null-object каталога: хост студии не подключён. Инструменты возвращают агенту понятное
    /// сообщение вместо исключения.
    /// </summary>
    public sealed class NullActivityCatalog : IActivityCatalog
    {
        public static readonly NullActivityCatalog Instance = new NullActivityCatalog();

        private static readonly ActivityInfo[] NoActivities = new ActivityInfo[0];
        private static readonly string[] NoCategories = new string[0];

        public int Count => 0;

        public IReadOnlyList<string> Categories => NoCategories;

        public IReadOnlyList<ActivityInfo> Search(string query, string category = null, int limit = 0)
            => NoActivities;

        public ActivityInfo Describe(string typeName) => null;
    }
}
