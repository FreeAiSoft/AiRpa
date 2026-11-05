using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Фоновый проход валидации (<see cref="DesignerValidationPass"/>): поколения, отмена, «не выполнено».
/// <para>
/// Это то, что отличает фоновую валидацию от «валидации в другом потоке». Без поколения результат
/// старого текста встаёт на активности нового (пользователь печатает — иконки прыгают по чужим
/// ошибкам); без отмены задача живёт после закрытия вкладки; без состояния «не выполнено» падение
/// прохода выглядит как «ошибок нет», и пользователь правит документ, считая его чистым.
/// </para>
/// </summary>
[TestFixture]
public class DesignerValidationPassTests
{
    [Test]
    public void NewPass_StartsInIdle()
    {
        DesignerValidationPass pass = new DesignerValidationPass();

        Assert.Multiple(() =>
        {
            Assert.That(pass.State, Is.EqualTo(DesignerValidationState.Idle));
            Assert.That(pass.Generation, Is.EqualTo(0));
            Assert.That(pass.IsRunning, Is.False);
            Assert.That(pass.Describe(), Does.Contain("не запускалось"),
                "«не запускалось» не должно выглядеть как «пройдено»");
        });
    }

    [Test]
    public void Begin_StartsRunningAndIncrementsGeneration()
    {
        DesignerValidationPass pass = new DesignerValidationPass();

        int first = pass.Begin(out CancellationToken token);
        int second = pass.Begin(out CancellationToken token2);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
            Assert.That(pass.State, Is.EqualTo(DesignerValidationState.Running));
            Assert.That(pass.IsRunning, Is.True);
            Assert.That(token2.IsCancellationRequested, Is.False);
        });
    }

    [Test]
    public void Begin_CancelsPreviousPass()
    {
        DesignerValidationPass pass = new DesignerValidationPass();

        pass.Begin(out CancellationToken first);
        pass.Begin(out _);

        Assert.That(first.IsCancellationRequested, Is.True,
            "новый проход обязан отменить предыдущий, иначе два прохода конкурируют за одни иконки");
    }

    [Test]
    public void TryComplete_StaleGeneration_IsRejected()
    {
        DesignerValidationPass pass = new DesignerValidationPass();

        int first = pass.Begin();
        pass.Begin();

        Assert.Multiple(() =>
        {
            Assert.That(pass.TryComplete(first), Is.False,
                "результат устаревшего прохода нельзя применять: он относится к прежнему тексту документа");
            Assert.That(pass.IsCurrent(first), Is.False);
            Assert.That(pass.State, Is.EqualTo(DesignerValidationState.Running));
        });
    }

    [Test]
    public void TryComplete_CurrentGeneration_SetsCompleted()
    {
        DesignerValidationPass pass = new DesignerValidationPass();
        int generation = pass.Begin();

        Assert.Multiple(() =>
        {
            Assert.That(pass.TryComplete(generation), Is.True);
            Assert.That(pass.State, Is.EqualTo(DesignerValidationState.Completed));
            Assert.That(pass.IsCompleted, Is.True);
            Assert.That(pass.Describe(), Does.Contain("выполнено"));
        });
    }

    [Test]
    public void TryFail_RecordsReason()
    {
        DesignerValidationPass pass = new DesignerValidationPass();
        int generation = pass.Begin();

        Assert.Multiple(() =>
        {
            Assert.That(pass.TryFail(generation, "битый снимок"), Is.True);
            Assert.That(pass.State, Is.EqualTo(DesignerValidationState.Failed));
            Assert.That(pass.FailureReason, Is.EqualTo("битый снимок"));
            Assert.That(pass.Describe(), Does.Contain("не выполнено").And.Contain("битый снимок"),
                "отчёт обязан называть причину: «не выполнено» без причины бесполезно");
        });
    }

    [Test]
    public void TryFail_StaleGeneration_IsRejected()
    {
        DesignerValidationPass pass = new DesignerValidationPass();

        int first = pass.Begin();
        pass.Begin();

        Assert.That(pass.TryFail(first, "старое падение"), Is.False,
            "падение устаревшего прохода не должно портить состояние текущего");
        Assert.That(pass.FailureReason, Is.Empty);
    }

    [Test]
    public void Cancel_MakesEverythingStale()
    {
        DesignerValidationPass pass = new DesignerValidationPass();
        int generation = pass.Begin(out CancellationToken token);

        pass.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(pass.IsCurrent(generation), Is.False);
            Assert.That(pass.State, Is.EqualTo(DesignerValidationState.Idle));
            Assert.That(pass.TryComplete(generation), Is.False,
                "после закрытия вкладки результат применять некуда");
        });
    }

    [Test]
    public void TokenFor_StaleGeneration_IsAlreadyCancelled()
    {
        DesignerValidationPass pass = new DesignerValidationPass();
        int first = pass.Begin();
        pass.Begin();

        Assert.That(pass.TokenFor(first).IsCancellationRequested, Is.True);
    }

    [Test]
    public void TryComplete_WithoutBegin_IsRejected()
    {
        DesignerValidationPass pass = new DesignerValidationPass();

        Assert.That(pass.TryComplete(0), Is.False, "поколение 0 — «прохода не было»");
    }
    [Test]
    public void SequentialPasses_ApplyOnlyOwnResult()
    {
        DesignerValidationPass pass = new DesignerValidationPass();
        List<int> applied = new List<int>();

        for (int index = 0; index < 3; index++)
        {
            int generation = pass.Begin(out _);
            if (pass.TryComplete(generation))
            {
                applied.Add(generation);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(pass.IsCompleted, Is.True);
        });
    }
}
