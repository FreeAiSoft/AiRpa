using System;
using System.Collections.Generic;
using System.Linq;

namespace RPA.Core.Services
{
    /// <summary>
    /// Когда <c>DesignerValidator</c> обязан выполнить полный проход валидации.
    /// <para>
    /// Иконка ошибки рисуется на <c>Border x:Name="validationVisuals"</c> внутри вьюхи активности, поэтому
    /// проход имеет смысл только для <b>реализованных</b> вьюх: у активности без вьюхи
    /// <c>ModelItem.View == null</c>. При открытии документа поверхность дизайнера строится позже
    /// (документ попадает в панель уже после <c>Load</c>, плюс раскладка и тема), поэтому единственный
    /// «инициализационный» проход по таймеру отрабатывал вхолостую: вьюх ещё нет — иконок нет, а флаг
    /// «инициализация выполнена» уже сброшен. Ошибки появлялись только после правки, когда модель
    /// менялась и проход запускался заново.
    /// </para>
    /// <para>
    /// Решение: полный проход (с разбором выражений Roslyn) выполняется, когда меняется модель,
    /// пока не выполнена инициализация, и когда меняется <b>подпись реализованных вьюх</b>
    /// (<see cref="BuildSignature"/>) — вьюха появилась, исчезла (свёрнутая активность) или была
    /// пересоздана. Между этими событиями таймер только пересчитывает подпись: обход модели без
    /// компиляции выражений, полный проход не повторяется (раньше валидация шла каждую секунду).
    /// </para>
    /// </summary>
    internal static class ValidationSchedule
    {
        /// <summary>
        /// Нужен ли полный проход валидации.
        /// <paramref name="currentSignature"/> = <c>null</c> означает «модель ещё не загружена»:
        /// проход не запускается, таймер повторит проверку на следующем тике.
        /// </summary>
        public static bool ShouldValidate(bool isModelChanged, bool isInitPending, string lastSignature, string currentSignature)
        {
            if (currentSignature == null)
            {
                return false;
            }

            return isModelChanged
                || isInitPending
                || !string.Equals(lastSignature, currentSignature, StringComparison.Ordinal);
        }

        /// <summary>
        /// Подпись набора реализованных вьюх: ключи сортируются, поэтому подпись не зависит от порядка
        /// обхода и меняется только по существу (вьюха появилась/исчезла/пересоздана).
        /// </summary>
        public static string BuildSignature(IEnumerable<string> realizedKeys)
        {
            if (realizedKeys == null)
            {
                return string.Empty;
            }

            List<string> keys = realizedKeys
                .Where(key => !string.IsNullOrEmpty(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            return string.Join("|", keys);
        }

        /// <summary>
        /// Ключ реализованной вьюхи. В ключ входит идентичность вьюхи: если WF пересоздал вьюху,
        /// локальные значения иконки потеряны и проход должен выполниться заново.
        /// </summary>
        public static string BuildViewKey(int index, string itemTypeName, int viewIdentity)
        {
            return index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":" + (itemTypeName ?? string.Empty)
                + ":" + viewIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
