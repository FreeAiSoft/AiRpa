using System;
using System.Collections.Generic;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт стенда по фоновой валидации дизайнера (шаги 0 и 5 плана
/// <c>dev-dev-docs\UI02_Background_Validation_Plan.md</c>).
/// <para>
/// Только данные: тесты утверждают по числам и адресам, а не по живым объектам WPF (они не переживают
/// границу домена стенда).
/// </para>
/// </summary>
[Serializable]
public sealed class DesignerBenchValidationReport
{
    /// <summary>Сценарий дошёл до конца.</summary>
    public bool Ok { get; set; }

    /// <summary>Человекочитаемое описание (цифры прохода + что было сделано).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Ошибки сценария (исключения стенда), а не замечания валидатора.</summary>
    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Проход выполнился сам, без единой правки документа.</summary>
    public bool FirstPassSeen { get; set; }

    /// <summary>Состояние прохода словами: «выполнено» / «не выполнено (причина)».</summary>
    public string PassState { get; set; } = string.Empty;

    /// <summary>Поколение прохода.</summary>
    public int Generation { get; set; }

    /// <summary>Активностей в области валидации (с телами и ветками).</summary>
    public int ScopeActivityCount { get; set; }

    /// <summary>Активностей по <c>WorkflowInspectionServices</c> — независимая сверка размера документа.</summary>
    public int InspectionActivityCount { get; set; }

    /// <summary>Снимков проверок выражений.</summary>
    public int CheckCount { get; set; }

    /// <summary>Замечаний фоновой фазы.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Адреса активностей, по которым есть замечания студии (включая вложенные).</summary>
    public List<string> ErrorAddresses { get; set; } = new List<string>();

    /// <summary>
    /// Текст подсказки иконки по адресу. По нему видно, <b>чья</b> иконка: своя (студии) или WF —
    /// у WF подсказка другая, поэтому одной видимости Border'а для проверки недостаточно.
    /// </summary>
    public Dictionary<string, string> AddressToolTips { get; set; } = new Dictionary<string, string>();

    public double ModelWalkMs { get; set; }

    public double SnapshotMs { get; set; }

    public double ValidateWorkflowMs { get; set; }

    public double ExpressionsMs { get; set; }

    public double ApplyMs { get; set; }

    public double UiMs { get; set; }

    public double OldUiMs { get; set; }

    /// <summary>Наибольшая пауза диспетчера за время прохода, мс.</summary>
    public double MaxDispatcherGapMs { get; set; }

    /// <summary>Сколько тиков пробника успело пройти.</summary>
    public int DispatcherTicks { get; set; }

    /// <summary>Адреса, где иконка ошибки видна.</summary>
    public List<string> IconVisibleAddresses { get; set; } = new List<string>();

    /// <summary>Адреса, где вьюха активности не построена (иконку ставить некуда).</summary>
    public List<string> NoViewAddresses { get; set; } = new List<string>();

    /// <summary>Адреса, где вьюха есть, а <c>validationVisuals</c> в ней не найден.</summary>
    public List<string> NoIconAddresses { get; set; } = new List<string>();

    /// <summary>Правка модели для проверки «правка → новый проход» выполнена.</summary>
    public bool ModelEdited { get; set; }

    /// <summary>После правки прошёл новый проход, и его результат актуален для нового поколения.</summary>
    public bool EditStartedNewPass { get; set; }

    public int GenerationAfterEdit { get; set; }

    /// <summary>Поколение сразу после <c>DetachValidation()</c> (закрытие вкладки).</summary>
    public int GenerationAfterDetach { get; set; }

    /// <summary>Сколько поколений появилось за 3 с после закрытия вкладки (обязано быть 0).</summary>
    public int PassesAfterDetach { get; set; }
}
