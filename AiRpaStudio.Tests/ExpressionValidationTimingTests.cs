using System;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «ошибки C#-выражений не показываются сразу».
/// <para>
/// Проверка выражения запускалась только фоновым циклом <c>BackgroundWorker</c> с паузой
/// <c>ValidationWaitTime</c> = 10 000 мс (<c>RoslynExpressionEditor.ValidatorDoWork</c>) и по потере
/// фокуса. Подписки на изменение текста не было, поэтому бейдж ошибки появлялся через десяток секунд
/// после набора (или после ухода фокуса).
/// </para>
/// <para>
/// Правило после исправления: проверка запускается по изменению текста, но с паузой-«дребезгом»
/// (debounce), чтобы не компилировать выражение на каждое нажатие; фоновый цикл остаётся резервным
/// путём с заметно меньшим периодом.
/// </para>
/// </summary>
public class ExpressionValidationTimingTests
{
    // --- Нужно ли проверять выражение. ---

    [Test]
    public void ShouldValidate_UnchangedText_ReturnsFalse()
    {
        // Иначе таймер и фоновый цикл компилировали бы Roslyn на каждый тик при неизменном тексте.
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionValidationTiming.ShouldValidate("a + 1", "a + 1"), Is.False);
            Assert.That(ExpressionValidationTiming.ShouldValidate(null, null), Is.False);
            Assert.That(ExpressionValidationTiming.ShouldValidate("", ""), Is.False);
            Assert.That(ExpressionValidationTiming.ShouldValidate(null, ""), Is.False,
                "null и пустая строка — одно и то же состояние поля");
        });
    }

    [Test]
    public void ShouldValidate_ChangedText_ReturnsTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionValidationTiming.ShouldValidate("a", "ab"), Is.True);
            Assert.That(ExpressionValidationTiming.ShouldValidate(null, "a"), Is.True);
            Assert.That(ExpressionValidationTiming.ShouldValidate("a", ""), Is.True,
                "очистка поля обязана погасить бейдж, а не оставить старую ошибку");
            Assert.That(ExpressionValidationTiming.ShouldValidate("", "a"), Is.True);
        });
    }

    // --- Паузы. ---

    [Test]
    public void Debounce_IsShortEnoughToSeeErrorWhileTyping()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionValidationTiming.DebounceMilliseconds,
                Is.InRange(ExpressionValidationTiming.MinDebounceMilliseconds, ExpressionValidationTiming.MaxDebounceMilliseconds));
            Assert.That(ExpressionValidationTiming.DebounceMilliseconds, Is.LessThanOrEqualTo(600),
                "пользователь должен увидеть ошибку, не успев допечатать строку");
            Assert.That(ExpressionValidationTiming.DebounceMilliseconds,
                Is.GreaterThanOrEqualTo(ExpressionValidationTiming.MinDebounceMilliseconds),
                "без паузы-дребезга Roslyn компилируется на каждое нажатие");
        });
    }

    [Test]
    public void PollInterval_IsReservePath_NotTenSeconds()
    {
        // Было 10 000 мс: при отсутствии подписки на текст это и была задержка появления бейджа.
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionValidationTiming.PollIntervalMilliseconds, Is.LessThanOrEqualTo(5000));
            Assert.That(ExpressionValidationTiming.PollIntervalMilliseconds, Is.GreaterThan(ExpressionValidationTiming.MaxDebounceMilliseconds),
                "резервный цикл не должен конкурировать с проверкой по изменению текста");
        });
    }
}
