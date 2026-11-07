using System;
using System.Collections.Generic;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт стенда «бейдж ошибки в поле C#-выражения появляется без клика» (баг «ошибка валидации
/// видна только после клика по полю»).
/// <para>
/// Замер делается в четырёх точках и <b>без единого клика</b>: сразу после открытия документа
/// (<c>загрузка</c>), после фонового прохода валидатора студии (<c>после прохода</c>), через несколько
/// секунд покоя (<c>покой</c>) и контрольной — после программного фокуса на поле (<c>после клика</c>,
/// это сегодняшнее поведение: бейдж появляется только здесь).
/// </para>
/// <para>
/// Только данные: тесты утверждают по значениям, а не по живым объектам WPF (они не переживают границу
/// домена стенда).
/// </para>
/// </summary>
[Serializable]
public sealed class DesignerBenchFieldBadgeReport
{
    /// <summary>Сценарий дошёл до конца.</summary>
    public bool Ok { get; set; }

    /// <summary>Человекочитаемое описание (что искали, сколько нашли).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Ошибки сценария (исключения стенда), а не замечания валидатора.</summary>
    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Текст невалидного выражения из фикстуры — по нему поле и опознаётся.</summary>
    public string BrokenText { get; set; } = string.Empty;

    /// <summary>Адрес активности с невалидным выражением (адреса — как в <c>DesignerValidationScope</c>).</summary>
    public string BrokenAddress { get; set; } = string.Empty;

    /// <summary>Замечания фонового прохода по этой активности (тексты иконки).</summary>
    public List<string> BrokenAddressMessages { get; set; } = new List<string>();

    /// <summary>Сколько полей <c>RoslynExpressionEditor</c> найдено в конструкторе сверх ожидаемых.</summary>
    public int ScopeActivityCount { get; set; }

    /// <summary>Точки замера по порядку: загрузка → проход → покой → клик.</summary>
    /// <summary>
    /// Что показывает окно «Свойства»: типы и тексты полей его дерева. Нужен, чтобы отличить «поле студии
    /// ещё не создано» от «поле есть, но бейджа нет» — от этого зависит, чей это дефект.
    /// </summary>
    public List<string> InspectorDump { get; set; } = new List<string>();


    public List<FieldBadgeStage> Stages { get; set; } = new List<FieldBadgeStage>();

    /// <summary>Состояние окна «Свойства» в сценарии: «ок» или причина, по которой снять замер не удалось.</summary>
    public string InspectorState { get; set; } = "не проверялось";
}

/// <summary>Одна точка замера: имя, прошедшее время и состояние всех найденных полей.</summary>
[Serializable]
public sealed class FieldBadgeStage
{
    /// <summary>Имя точки: <c>загрузка</c>, <c>после прохода</c>, <c>покой</c>, <c>после клика</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Сколько миллисекунд прошло от открытия документа.</summary>
    public double ElapsedMs { get; set; }

    /// <summary>Поля конструктора на этой точке.</summary>
    public List<FieldBadgeEntry> Fields { get; set; } = new List<FieldBadgeEntry>();

    /// <summary>Поля окна «Свойства» на этой точке (если окно удалось показать).</summary>
    public List<FieldBadgeEntry> InspectorFields { get; set; } = new List<FieldBadgeEntry>();
}

/// <summary>Состояние одного поля выражения на одной точке замера.</summary>
[Serializable]
public sealed class FieldBadgeEntry
{
    /// <summary>Где поле: <c>конструктор</c> или <c>свойства</c>.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Имя элемента поля (у <c>ExpressionTextBox</c> — имя аргумента).</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Текст поля.</summary>
    /// <summary>Имя аргумента активности, к которому относится поле (сопоставление по модели).</summary>
    public string ArgumentName { get; set; } = string.Empty;


    public string Text { get; set; } = string.Empty;

    /// <summary>Какой шаблон показан: <c>textblock</c> (не в правке) или <c>expressioneditor</c> (в правке).</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Наш бейдж: <c>HasValidationError</c>.</summary>
    public bool HasBadge { get; set; }

    /// <summary>Текст подсказки бейджа (<c>ValidationErrorMessage</c>).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Видимость глифа <c>MaterialIcon</c> в поле — то, что видит пользователь.</summary>
    public string BadgeVisibility { get; set; } = string.Empty;

    /// <summary>Адаптер WPF: <c>Validation.HasError</c> на самом поле.</summary>
    public bool WpfHasError { get; set; }

    /// <summary>Тип элемента модели выражения (для опознания поля: <c>CSharpValue&lt;T&gt;</c> и т.п.).</summary>
    public string ExpressionItemType { get; set; } = string.Empty;

    /// <summary>Режим правки (<c>EditingState</c>).</summary>
    public string EditingState { get; set; } = string.Empty;
}
