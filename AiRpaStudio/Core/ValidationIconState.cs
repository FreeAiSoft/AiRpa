using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Validation;
using RPA.Helpers;

namespace RPA.Core.Services
{
    /// <summary>
    /// Решение «кто рисует иконку валидации на активности».
    /// <para>
    /// Иконку ошибки/предупреждения на активности рисует сам WF: в шаблоне
    /// <c>WorkflowElementDesignerDefaultTemplate</c> у <c>Border x:Name="validationVisuals"</c> есть триггеры
    /// на <c>ModelItem.ValidationState</c> и подпись из <c>ModelItem.ValidationMessage</c>. Студия
    /// (<c>DesignerValidator.SetValidationIconDirectly</c>) до этого правила ставила на тот же Border
    /// <b>локальные</b> значения — а локальное значение сильнее триггера шаблона: при отсутствии «своих»
    /// ошибок студия писала <c>Visibility = Collapsed; Background = null; ToolTip = null</c> и гасила
    /// нативную красную иконку WF. Отсюда баг «ошибки валидации исчезают, хотя должны быть»
    /// (в проектах Visual Basic иконка оставалась красной, в C# — перекрашивалась в студийную).
    /// </para>
    /// <para>
    /// Правило после исправления:
    /// <list type="bullet">
    /// <item>своих ошибок нет — Border не трогаем (снимаем локальные значения), рисует WF;</item>
    /// <item>свои ошибки есть — показываем иконку сами и объединяем тексты в подсказке.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class ValidationIconState
    {
        /// <summary>
        /// Нужно ли рисовать иконку своими силами. <c>false</c> — не вмешиваться и отдать отрисовку WF:
        /// иначе нативная иконка ошибки будет погашена.
        /// </summary>
        public static bool ShouldDrawOwn(IReadOnlyCollection<string> studioErrors)
        {
            return studioErrors != null && studioErrors.Count > 0;
        }

        /// <summary>
        /// Текст подсказки иконки: ошибки студии (по одной в строке) плюс сообщение WF, если оно есть.
        /// Пустые строки и дубликаты отбрасываются. <c>null</c> — показывать нечего.
        /// </summary>
        public static string ComposeToolTip(IEnumerable<string> studioErrors, string workflowMessage)
        {
            List<string> lines = new List<string>();

            foreach (string error in studioErrors ?? Enumerable.Empty<string>())
            {
                AppendLine(lines, error);
            }

            AppendLine(lines, workflowMessage);

            return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
        }

        private static void AppendLine(List<string> lines, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string line = value.Trim();
            if (!lines.Any(l => string.Equals(l, line, StringComparison.Ordinal)))
            {
                lines.Add(line);
            }
        }

        /// <summary>
        /// Кисть иконки ошибки: единственный не-нейтральный цвет палитры (красный — системная семантика
        /// ошибки). Раньше брался отражением из <c>WorkflowDesignerIcons.DesignerItems.ActivityErrorValidation</c>
        /// (стиль «Windows Store App»), поэтому иконка студии выбивалась из палитры.
        /// </summary>
        public static Brush ErrorBrush()
        {
            return WfDesignerTheme.ErrorBrush(StudioPalette.Active);
        }

        /// <summary>
        /// Кисть иконки ошибки на активности — родной рисунок WF
        /// (<c>WorkflowDesignerIcons.DesignerItems.ActivityErrorValidation</c>: красный круг с белым «!»).
        /// <para>
        /// Именно её WF ставит в <c>Border.Background</c> шаблона <c>WorkflowElementDesignerDefaultTemplate</c>,
        /// поэтому иконка выглядит так же, как в проектах Visual Basic. Сплошная кисть палитры
        /// (<see cref="ErrorBrush"/>) — только резерв, если свойство иконок WF недоступно: раньше она
        /// ставилась всегда, и на активности получался красный прямоугольник без глифа
        /// (баг «только красная рамка без иконки»).
        /// </para>
        /// <para>
        /// Кисть клонируется и замораживается: экземпляр ставится на десятки активностей, а изменяемый
        /// общий <c>DrawingBrush</c> WF отдавать наружу нельзя.
        /// </para>
        /// </summary>
        public static Brush ErrorIconBrush()
        {
            Brush native = TryGetWorkflowErrorIcon();
            return native == null ? ErrorBrush() : CloneFrozen(native) ?? native;
        }

        /// <summary>
        /// Рисунок иконки ошибки WF. Читается отражением: <c>DesignerItems</c> — вложенный тип
        /// <c>WorkflowDesignerIcons</c>. Любая ошибка отражения даёт <c>null</c> (тогда работает резерв).
        /// </summary>
        private static Brush TryGetWorkflowErrorIcon()
        {
            try
            {
                Type designerItems = typeof(System.Activities.Presentation.WorkflowDesignerIcons)
                    .GetNestedType("DesignerItems", BindingFlags.Public | BindingFlags.NonPublic);

                PropertyInfo property = designerItems?.GetProperty(
                    "ActivityErrorValidation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                return property?.GetValue(null) as Brush;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Brush CloneFrozen(Brush brush)
        {
            try
            {
                Brush clone = brush.CloneCurrentValue();
                clone.Freeze();
                return clone;
            }
            catch (Exception)
            {
                return null;
            }
        }


        /// <summary>
        /// Сообщение валидации WF для активности (обязательные свойства, валидаторы активностей,
        /// «дочерняя активность некорректна»). В WPF-биндинге WF берёт его из
        /// <c>ModelItem.ValidationMessage</c>, но публичного свойства у <see cref="ModelItem"/> нет —
        /// <c>ValidationService.GetValidationMessage</c> приватный, поэтому читаем отражением.
        /// Метод диагностический: любая ошибка отражения даёт <c>null</c>, а не исключение.
        /// </summary>
        public static string TryGetWorkflowMessage(ValidationService validationService, ModelItem modelItem)
        {
            if (validationService == null || modelItem == null)
            {
                return null;
            }

            try
            {
                MethodInfo method = validationService.GetType().GetMethod(
                    "GetValidationMessage",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(ModelItem) },
                    null);

                return method?.Invoke(validationService, new object[] { modelItem }) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
