using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
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
        /// Кисть иконки ошибки на активности — красный круг с белым «!» (семантика ошибки WF по умолчанию).
        /// <para>
        /// Рисуется на месте, а не читается из <c>WorkflowDesignerIcons.DesignerItems.ActivityErrorValidation</c>:
        /// студия включает стиль «Windows Store App» (<c>UseWindowsStoreAppStyleIcons</c>), и тот же ключ
        /// возвращает синюю иконку, из-за чего цвет ошибки зависел от порядка запуска.
        /// </para>
        /// </summary>
        public static Brush ErrorIconBrush()
        {
            return CreateErrorIcon();
        }

        /// <summary>
        /// Детерминированная кисть ошибки: красный круг с белым «!» (семантика ошибки WF по умолчанию).
        /// <para>
        /// Строится на месте, а не читается из <c>WorkflowDesignerIcons.DesignerItems.ActivityErrorValidation</c>:
        /// студия включает стиль «Windows Store App» (<c>UseWindowsStoreAppStyleIcons</c>), и тот же ключ
        /// возвращает синюю иконку; отражение делало цвет ошибки зависимым от порядка запуска.
        /// </para>
        /// <para>
        /// Кисть замораживается: экземпляр ставится на десятки активностей, а изменяемый общий объект
        /// отдавать наружу нельзя.
        /// </para>
        /// </summary>
        private static Brush CreateErrorIcon()
        {
            var group = new DrawingGroup();

            // Красный круг — системная семантика ошибки; цвет берётся из палитры, а не из стиля WF.
            Color red = WfDesignerTheme.ErrorBrush(StudioPalette.Active).Color;
            group.Children.Add(new GeometryDrawing(
                new SolidColorBrush(red),
                null,
                new EllipseGeometry(new Point(7, 7), 7, 7)));

            // Белый «!» поверх круга.
            var glyph = new FormattedText(
                "!",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                Brushes.White,
                1.0);
            group.Children.Add(new GeometryDrawing(
                Brushes.White,
                null,
                glyph.BuildGeometry(new Point(4.4, 1.4))));

            var brush = new DrawingBrush(group);
            brush.Freeze();
            return brush;
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
