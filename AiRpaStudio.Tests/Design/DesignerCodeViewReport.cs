using System;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт live-проверки «дизайнер → редактор кода» на живом <see cref="RPA.Core.WorkflowDesignerEx"/>:
/// буфер дизайнера (и признак «изменён») переносится в <see cref="RPA.Models.CodeDocument"/> той же вкладки,
/// а дизайнер отсоединяется от модели. Только данные — тесты утверждают по ним.
/// </summary>
[Serializable]
public sealed class DesignerCodeViewReport
{
    public string Error { get; set; }

    /// <summary>Файл загружен в конструктор loose-путём.</summary>
    public bool LoadSucceeded { get; set; }

    /// <summary>Реальная правка модели пометила дизайнер «изменённым».</summary>
    public bool EditedAfterModelChange { get; set; }

    /// <summary><c>Flush()</c> отдал непустой сериализованный XAML.</summary>
    public bool XamlTextNonEmpty { get; set; }

    /// <summary>После переключения в код дизайнер удалён из <c>WorkflowDesigners</c>.</summary>
    public bool DesignerDetached { get; set; }

    /// <summary>Для файла открыт <see cref="RPA.Models.CodeDocument"/>.</summary>
    public bool CodeDocumentOpened { get; set; }

    /// <summary>Признак «изменён» перенесён из дизайнера в код-документ.</summary>
    public bool DirtyCarriedOver { get; set; }

    /// <summary>Текст редактора равен сериализованному буферу дизайнера.</summary>
    public bool TextMatchesBuffer { get; set; }

    public string Description { get; set; } = string.Empty;
}
