using System.Collections.Generic;
using NUnit.Framework;
using RPA.Chat.Services.Workflow;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-05: проверка выражений до правки модели — правила студии (чистая политика), синтаксис на Roslyn
    /// (C# и VB) и определение языка документа.
    /// <para>
    /// Проверки headless: выражение разбирается как текст, дизайнер не нужен. Тесты фиксируют главное
    /// свойство — «неизвестное имя» не считается ошибкой синтаксиса (имена проверяет валидация WF после
    /// правки), а опечатка в самом выражении ловится сразу и на обоих языках.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowExpressionTests
    {
        // --- Политика студии ---

        [Test]
        public void Policy_AcceptsPlainExpression()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Describe("text.ToUpper()", WorkflowExpressionLanguage.CSharp), Is.Empty);
                Assert.That(Describe("\"a\" & \"b\"", WorkflowExpressionLanguage.VisualBasic), Is.Empty);
            });
        }

        [Test]
        public void Policy_RejectsEmptyExpression()
        {
            Assert.That(Describe("   ", WorkflowExpressionLanguage.CSharp), Does.Contain("не задано"));
        }

        [Test]
        public void Policy_RejectsLambdaInBothLanguages()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Describe("items.Select(x => x)", WorkflowExpressionLanguage.CSharp),
                    Does.Contain("лямбды"), "лямбда в C# запрещена: узел не переживает сериализацию");
                Assert.That(Describe("items.Select(Function(x) x)", WorkflowExpressionLanguage.VisualBasic),
                    Does.Contain("лямбды"), "лямбда в VB запрещена по тому же правилу");
            });
        }

        [Test]
        public void Policy_RejectsUnbalancedBracketsAndQuotes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Describe("Math.Max(1, 2", WorkflowExpressionLanguage.CSharp),
                    Does.Contain("не закрыта открывающая скобка"));
                Assert.That(Describe("1 + 2)", WorkflowExpressionLanguage.CSharp),
                    Does.Contain("без открывающей"));
                Assert.That(Describe("\"abc", WorkflowExpressionLanguage.CSharp),
                    Does.Contain("не закрыта кавычка"));
            });
        }

        [Test]
        public void Policy_RejectsTrailingOperator()
        {
            Assert.That(Describe("total +", WorkflowExpressionLanguage.CSharp), Does.Contain("обрывается на операторе"));
        }

        /// <summary>Путаница языков — частая ошибка агента: выражение на VB в C#-документе.</summary>
        [Test]
        public void Policy_DetectsLanguageMixUp()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Describe("left AndAlso right", WorkflowExpressionLanguage.CSharp),
                    Does.Contain("Visual Basic"), "VB-синтаксис в C#-документе ловится политикой");
                Assert.That(Describe("left && right", WorkflowExpressionLanguage.VisualBasic),
                    Does.Contain("C#"), "C#-синтаксис в VB-документе ловится политикой");
            });
        }

        /// <summary>Строка в кавычках — не выражение-ошибка: внутри литерала скобки и стрелки не считаются.</summary>
        [Test]
        public void Policy_IgnoresBracketsInsideStringLiteral()
        {
            Assert.That(Describe("\"a ( b \\\" c\"", WorkflowExpressionLanguage.CSharp), Is.Empty);
        }

        // --- Синтаксис на Roslyn ---

        [Test]
        public void Syntax_CSharp_AcceptsValidExpression()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionSyntax.Check("text.ToUpper()", WorkflowExpressionLanguage.CSharp), Is.Empty);
                Assert.That(WorkflowExpressionSyntax.Check("1 + 2 * (3 - 4)", WorkflowExpressionLanguage.CSharp), Is.Empty);
                Assert.That(WorkflowExpressionSyntax.Check("total >= 10 && name != null", WorkflowExpressionLanguage.CSharp), Is.Empty);
            });
        }

        /// <summary>
        /// Разбор ловит то, что не видит политика: ключевое слово вместо выражения, обрыв, скобки.
        /// <para>
        /// Граница проверки зафиксирована отдельно: <c>1 + * 2</c> на уровне <b>синтаксиса</b> Roslyn
        /// принимает (в его грамматике <c>*</c> — разыменование указателя), поэтому такая ошибка доходит
        /// до валидации WF, а не отбрасывается инструментом. Проверка разбора не привязывает имена и типы
        /// намеренно: иначе пришлось бы заново реализовывать семантику TextExpression.
        /// </para>
        /// </summary>
        [Test]
        public void Syntax_CSharp_RejectsBrokenExpression()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionSyntax.Check("1 +", WorkflowExpressionLanguage.CSharp), Is.Not.Empty);
                Assert.That(WorkflowExpressionSyntax.Check("(1 + 2", WorkflowExpressionLanguage.CSharp), Is.Not.Empty);
                Assert.That(WorkflowExpressionSyntax.Check("if (x)", WorkflowExpressionLanguage.CSharp), Is.Not.Empty,
                    "инструкция вместо выражения — ошибка запроса, а не значение аргумента");
                Assert.That(WorkflowExpressionSyntax.Check("if", WorkflowExpressionLanguage.CSharp), Is.Not.Empty,
                    "ключевое слово вместо выражения: политика такое не ловит, ловит разбор");
            });
        }

        /// <summary>Неизвестное имя — не синтаксис: его проверяет валидация WF после правки, не отказывая заранее.</summary>
        [Test]
        public void Syntax_CSharp_AllowsUnknownIdentifiers()
        {
            Assert.That(WorkflowExpressionSyntax.Check("unknownVariable + Text", WorkflowExpressionLanguage.CSharp), Is.Empty);
        }

        [Test]
        public void Syntax_VisualBasic_AcceptsValidExpression()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionSyntax.Check("\"a\" & \"b\"", WorkflowExpressionLanguage.VisualBasic), Is.Empty);
                Assert.That(WorkflowExpressionSyntax.Check("left AndAlso right", WorkflowExpressionLanguage.VisualBasic), Is.Empty);
            });
        }

        [Test]
        public void Syntax_VisualBasic_RejectsBrokenExpression()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionSyntax.Check("1 +", WorkflowExpressionLanguage.VisualBasic), Is.Not.Empty);
                Assert.That(WorkflowExpressionSyntax.Check("left && right", WorkflowExpressionLanguage.VisualBasic), Is.Not.Empty,
                    "синтаксис C# в VB-документе не разбирается");
            });
        }

        // --- Язык документа ---

        [Test]
        public void Detector_ReadsLanguageFromDocument()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionLanguageDetector.OfText("<Activity xmlns=\"…\"><WriteLine Text=\"a\" /></Activity>"),
                    Is.EqualTo(WorkflowExpressionLanguage.CSharp));
                Assert.That(WorkflowExpressionLanguageDetector.OfText(
                        "<Activity xmlns:mv=\"clr-namespace:Microsoft.VisualBasic;assembly=System\">"),
                    Is.EqualTo(WorkflowExpressionLanguage.VisualBasic));
                Assert.That(WorkflowExpressionLanguageDetector.OfText(
                        "<VisualBasicValue x:TypeArguments=\"x:String\">a &amp; b</VisualBasicValue>"),
                    Is.EqualTo(WorkflowExpressionLanguage.VisualBasic));
            });
        }

        [Test]
        public void Detector_ExplicitLanguageWins()
        {
            const string csharpDocument = "<Activity xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\">";

            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionLanguageDetector.OfText(csharpDocument),
                    Is.EqualTo(WorkflowExpressionLanguage.CSharp));

                IReadOnlyList<string> problems = WorkflowExpressionPolicy.Check("1 +", WorkflowExpressionLanguage.VisualBasic);
                Assert.That(problems, Is.Not.Empty, "политика обязана проверять явно заданный язык");
            });
        }

        [Test]
        public void LanguageNames_ParseAliases()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowExpressionLanguages.TryParse("C#", out WorkflowExpressionLanguage? sharp), Is.True);
                Assert.That(sharp, Is.EqualTo(WorkflowExpressionLanguage.CSharp));

                Assert.That(WorkflowExpressionLanguages.TryParse("vb.net", out WorkflowExpressionLanguage? basic), Is.True);
                Assert.That(basic, Is.EqualTo(WorkflowExpressionLanguage.VisualBasic));

                Assert.That(WorkflowExpressionLanguages.TryParse(null, out WorkflowExpressionLanguage? unset), Is.True);
                Assert.That(unset, Is.Null, "пустое имя языка значит «определи по документу»");

                Assert.That(WorkflowExpressionLanguages.TryParse("pascal", out _), Is.False);
            });
        }

        private static string Describe(string expression, WorkflowExpressionLanguage language)
        {
            return WorkflowExpressionPolicy.Describe(WorkflowExpressionPolicy.Check(expression, language));
        }
    }
}
