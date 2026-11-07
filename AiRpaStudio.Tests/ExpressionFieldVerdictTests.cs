using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «ошибка валидации поля видна только после клика по полю» — механизм доставки вердикта.
/// <para>
/// Фоновая проверка документа уже компилировала каждое выражение и ставила иконку на активность, но до
/// самого поля результат не доходил: поле узнавало об ошибке только от собственных триггеров (изменение
/// текста и фокус, то есть клик). <see cref="ExpressionFieldVerdicts"/> доводит посчитанный вердикт до
/// поля — без второй компиляции Roslyn на UI-потоке.
/// </para>
/// <para>
/// Второй набор проверок — стражи исходников: они держат правило «поле показывает вердикт по загрузке»
/// для <b>обеих</b> поверхностей. Окно «Свойства» строит поле аргумента тогда, когда свойство показано,
/// и это поле — тот же <c>RoslynExpressionEditor</c>: живой стенд (<c>ExpressionFieldBadgeTests</c>)
/// подтверждает, что бейдж в нём появляется без клика по полю.
/// </para>
/// </summary>
[TestFixture]
public class ExpressionFieldVerdictTests
{
    private const string EditorSourcePath = @"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionEditor.xaml.cs";
    private const string ValidatorSourcePath = @"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs";
    private const string VerdictsSourcePath = @"AiRpaStudio\Core\Validation\ExpressionFieldVerdicts.cs";

    [SetUp]
    public void ClearRegistry()
    {
        ExpressionFieldVerdicts.Reset();
    }

    // --- Доставка вердикта. ---

    [Test]
    public void Publish_GivesErrorToField()
    {
        object key = new object();
        var field = new FakeField(key);

        ExpressionFieldVerdicts.Set(key, "Имя \"аааа\" не существует в текущем контексте.");
        ExpressionFieldVerdicts.Register(field);
        ExpressionFieldVerdicts.Publish();

        Assert.Multiple(() =>
        {
            Assert.That(field.Applied, Has.Count.EqualTo(1), "бейдж поля не обновился");
            Assert.That(field.Applied[0], Does.Contain("аааа"), "поле получило не тот текст ошибки");
        });
    }

    /// <summary>
    /// «Проверено, ошибок нет» обязано снимать бейдж: иначе исправленное выражение оставляло бы ошибку
    /// от прошлого прохода (этот дефект в поле уже был исправлен для собственной проверки редактора).
    /// </summary>
    [Test]
    public void Publish_ClearsBadgeWhenExpressionBecameValid()
    {
        object key = new object();
        var field = new FakeField(key);

        ExpressionFieldVerdicts.Set(key, "ошибка");
        ExpressionFieldVerdicts.Register(field);
        ExpressionFieldVerdicts.Publish();
        ExpressionFieldVerdicts.Set(key, null);
        ExpressionFieldVerdicts.Publish();

        Assert.That(field.Applied, Is.EqualTo(new List<string> { "ошибка", null }),
            "вердикт «ошибок нет» должен приходить в поле как снятие бейджа");
    }

    /// <summary>Нет вердикта — нет и записи в поле: «ещё не проверяли» и «ошибок нет» — разные состояния.</summary>
    [Test]
    public void Publish_LeavesFieldUntouchedWithoutVerdict()
    {
        var field = new FakeField(new object());

        ExpressionFieldVerdicts.Register(field);
        ExpressionFieldVerdicts.Publish();

        Assert.That(field.Applied, Is.Empty, "поле без вердикта не должно получать никакого состояния");
    }

    /// <summary>
    /// Поле в правке получает вердикты из собственной проверки редактора: документный вердикт не должен
    /// с ним спорить (иначе бейдж мигал бы на каждое нажатие).
    /// </summary>
    [Test]
    public void Publish_SkipsFieldBeingEdited()
    {
        object key = new object();
        var field = new FakeField(key) { IsBeingEdited = true };

        ExpressionFieldVerdicts.Set(key, "ошибка");
        ExpressionFieldVerdicts.Register(field);
        ExpressionFieldVerdicts.Publish();

        Assert.That(field.Applied, Is.Empty, "поле в правке не должно получать документный вердикт");
    }

    [Test]
    public void Unregister_StopsDelivery()
    {
        object key = new object();
        var field = new FakeField(key);

        ExpressionFieldVerdicts.Set(key, "ошибка");
        ExpressionFieldVerdicts.Register(field);
        ExpressionFieldVerdicts.Unregister(field);
        ExpressionFieldVerdicts.Publish();

        Assert.Multiple(() =>
        {
            Assert.That(field.Applied, Is.Empty, "снятое с регистрации поле получило вердикт");
            Assert.That(ExpressionFieldVerdicts.FieldCount, Is.EqualTo(0), "реестр полей не очистился");
        });
    }

    [Test]
    public void TryGet_DistinguishesUnknownFromNoError()
    {
        object withError = new object();
        object withoutError = new object();
        var unknown = new object();

        ExpressionFieldVerdicts.Set(withError, "ошибка");
        ExpressionFieldVerdicts.Set(withoutError, null);

        Assert.Multiple(() =>
        {
            Assert.That(ExpressionFieldVerdicts.TryGet(withError, out string error), Is.True);
            Assert.That(error, Is.EqualTo("ошибка"));
            Assert.That(ExpressionFieldVerdicts.TryGet(withoutError, out string none), Is.True);
            Assert.That(none, Is.Null, "«ошибок нет» — это вердикт с пустым текстом");
            Assert.That(ExpressionFieldVerdicts.TryGet(unknown, out _), Is.False, "неизвестное выражение — не вердикт");
        });
    }

    [Test]
    public void Forget_DropsStaleVerdict()
    {
        object key = new object();
        var field = new FakeField(key);

        ExpressionFieldVerdicts.Set(key, "ошибка");
        ExpressionFieldVerdicts.Forget(key);
        ExpressionFieldVerdicts.Register(field);
        ExpressionFieldVerdicts.Publish();

        Assert.That(field.Applied, Is.Empty, "устаревший вердикт применён после правки выражения");
    }

    /// <summary>Реестр не держит поля живыми: ссылки слабые, иначе закрытые документы остались бы в памяти.</summary>
    [Test]
    public void Registry_HoldsFieldsWeakly()
    {
        RegisterTemporaryField();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(ExpressionFieldVerdicts.FieldCount, Is.EqualTo(0),
            "реестр удерживает собранное поле — утечка визуальных деревьев");
    }

    // --- Стражи исходников: поле показывает вердикт по загрузке, а не по фокусу. ---

    [Test]
    public void Field_AppliesVerdictOnLoad()
    {
        string source = Read(EditorSourcePath);
        string loaded = ExtractMethod(source, "private void OnTextBlockLoaded");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Does.Contain("ExpressionFieldVerdicts.Register(this)"),
                "поле не подписывается на вердикты документной проверки при загрузке");
            Assert.That(loaded, Does.Contain("ApplyStoredVerdict()"),
                "поле не показывает готовый вердикт при загрузке — бейдж снова появится только по клику");
        });
    }

    [Test]
    public void Field_StopsReceivingVerdictsWhenClosed()
    {
        string source = Read(EditorSourcePath);
        string closing = ExtractMethod(source, "private void OnEditorClosing");
        string unloaded = ExtractMethod(source, "private void OnEditorUnloaded");

        Assert.Multiple(() =>
        {
            Assert.That(closing, Does.Contain("ExpressionFieldVerdicts.Unregister(this)"),
                "закрытое поле остаётся в реестре вердиктов");
            Assert.That(unloaded, Does.Contain("ExpressionFieldVerdicts.Unregister(this)"),
                "перестроенное поле остаётся в реестре вердиктов");
        });
    }

    /// <summary>Пока поле правит пользователь, бейджем распоряжается собственная проверка редактора.</summary>
    [Test]
    public void Field_IgnoresVerdictWhileBeingEdited()
    {
        string source = Read(EditorSourcePath);
        string apply = ExtractMethod(source, "internal void ApplyStoredVerdict");

        Assert.That(apply, Does.Contain("EditingState != EditingState.Idle"),
            "поле в правке не должно принимать документный вердикт");
    }

    [Test]
    public void Validator_PublishesFieldVerdictsInApplyPhase()
    {
        string source = Read(ValidatorSourcePath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("PublishFieldVerdicts(scope, errors)"),
                "результат прохода снова доходит только до иконки активности");
            Assert.That(source, Does.Contain("ExpressionFieldVerdicts.Publish()"),
                "раздача вердиктов полям не выполняется");
        });
    }

    /// <summary>Вердикт поля — текст без префикса аргумента; префикс остаётся подсказкой иконки.</summary>
    [Test]
    public void ValidationError_CarriesArgumentNameAndCleanDetail()
    {
        string source = Read(VerdictsSourcePath);
        string checks = Read(@"AiRpaStudio\Core\Validation\ActivityExpressionCheck.cs");
        string evaluator = Read(@"AiRpaStudio\Core\Validation\ExpressionErrorEvaluator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(checks, Does.Contain("internal string ArgumentName"), "замечание не знает, к какому аргументу относится");
            Assert.That(checks, Does.Contain("internal string Detail"), "у замечания нет текста без префикса аргумента");
            Assert.That(evaluator, Does.Contain("check.ArgumentName"), "оценщик не заполняет имя аргумента в замечании");
            Assert.That(source, Does.Contain("IExpressionFieldBadge"), "нет контракта поля, которому можно показать вердикт");
        });
    }

    // --- Вспомогательное. ---

    /// <summary>Поле в реестре, которое заведомо переживёт только текущую область видимости.</summary>
    private static void RegisterTemporaryField()
    {
        ExpressionFieldVerdicts.Register(new FakeField(new object()));
        ExpressionFieldVerdicts.Register(new FakeField(new object()));
        Assert.That(ExpressionFieldVerdicts.FieldCount, Is.EqualTo(2), "поля не зарегистрированы");
    }

    private sealed class FakeField : IExpressionFieldBadge
    {
        internal FakeField(object key)
        {
            ExpressionKey = key;
        }

        public object ExpressionKey { get; }

        public bool IsBeingEdited { get; set; }

        internal List<string> Applied { get; } = new List<string>();

        public void ApplyVerdict(string message)
        {
            Applied.Add(message);
        }
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"не найден метод {signature}");

        int end = source.IndexOf("\n        private", start, StringComparison.Ordinal);
        return end > start ? source.Substring(start, end - start) : source.Substring(start);
    }

    private static string Read(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
