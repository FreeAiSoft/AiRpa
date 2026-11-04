using System.Text;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: песочница Roslyn
/// (<c>RoslynSandbox</c>).
/// <para>
/// Код агента приходит из недоверенного источника, поэтому проверяется не «компилируется ли он», а
/// «что именно песочница запрещает»: ввод-вывод, рефлексию, сеть, диагностику, unsafe и P-Invoke,
/// а также размер кода и время компиляции. Диагностика обязана быть машиночитаемой — с кодом, строкой,
/// столбцом и подсказкой, чтобы агент исправлялся сам.
/// </para>
/// </summary>
[TestFixture]
public class RoslynSandboxTests
{
    private const string SimpleCode = @"
using System;
using System.Activities;
using System.Activities.Statements;

public static class Workflow
{
    public static Activity Build()
    {
        return new Sequence();
    }
}
";

    private static readonly string[] BannedDirectives =
    {
        "using System.IO;",
        "using System.Reflection;",
        "using System.Net;",
        "using System.Diagnostics;"
    };

    private RoslynSandbox _sandbox;

    [SetUp]
    public void CreateSandbox()
    {
        _sandbox = new RoslynSandbox();
    }

    [Test]
    public void Compile_SimpleCode_ReturnsLoadedAssembly()
    {
        RoslynCompileResult result = _sandbox.Compile(SimpleCode);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Assembly, Is.Not.Null, "сборка кода обязана быть загружена");
            Assert.That(result.AssemblyName, Does.StartWith("RpaGeneratedWorkflow_"));
            Assert.That(result.Errors, Is.Empty);
        });
    }

    [Test]
    public void Compile_CollectsNamespacesAndTheirAssemblies()
    {
        const string code = @"
using System;
using System.Activities;
using System.Activities.Statements;
using System.Text.RegularExpressions;

public static class Workflow
{
    public static Activity Build()
    {
        Regex.IsMatch(""x"", ""x"");
        return new Sequence();
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Namespaces, Does.Contain("System.Text.RegularExpressions"),
                "пространства имён из using попадают в TextExpression.NamespacesForImplementation");
            Assert.That(result.Namespaces, Does.Contain("System.Activities"));
            Assert.That(result.References, Does.Contain("System"),
                "ссылки строятся из реально использованных сборок, а не из догадок");
            Assert.That(result.References, Does.Contain("System.Activities"));
        });
    }

    [TestCaseSource(nameof(BannedDirectives))]
    public void Compile_BannedNamespace_IsRejected(string directive)
    {
        string code = directive + Environment.NewLine + SimpleCode;

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.That(result.Ok, Is.False, "код с " + directive + " обязан быть отклонён песочницей");

        Assert.Multiple(() =>
        {
            Assert.That(result.Assembly, Is.Null, "запрещённый код не должен попадать в сборку");
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("запрещённое пространство имён"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("песочниц"),
                "агент получает объяснение, что именно запрещено");
        });
    }

    [Test]
    public void Compile_FullyQualifiedBannedCall_IsRejected()
    {
        const string code = @"
using System.Activities;
using System.Activities.Statements;

public static class Workflow
{
    public static Activity Build()
    {
        var text = System.IO.File.ReadAllText(""C:\\Windows\\win.ini"");
        return new Sequence { DisplayName = text };
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "полное имя System.IO.File обходит using и обязано отклоняться");
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("System.IO"));
        });
    }

    [Test]
    public void Compile_UnsafeCode_IsRejected()
    {
        const string code = @"
public static class Unsafe
{
    public static unsafe int Read(int* pointer)
    {
        return *pointer;
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("unsafe"));
        });
    }

    [Test]
    public void Compile_DllImport_IsRejected()
    {
        const string code = @"
using System.Runtime.InteropServices;

public static class Native
{
    [DllImport(""kernel32"")]
    public static extern int GetTickCount();
}
";

        // Пространство имён разрешаем: сработать обязан именно запрет [DllImport], а не список using.
        RoslynSandboxOptions options = RoslynSandboxOptions.CreateDefault();
        options.BannedNamespaces.Remove("System.Runtime.InteropServices");

        RoslynCompileResult result = new RoslynSandbox(options).Compile(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "P-Invoke в песочнице запрещён");
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("DllImport"),
                "запрет неуправляемого кода не зависит от списка пространств имён");
        });
    }

    [Test]
    public void Compile_SyntaxError_ReportsMachineReadableDiagnostic()
    {
        const string code = @"
public static class Workflow
{
    public static void Build(
    {
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.That(result.Ok, Is.False);

        RoslynDiagnostic first = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null, "ошибка синтаксиса обязана дать диагностику");
            Assert.That(first.Id, Does.StartWith("CS"), "агент получает код Roslyn");
            Assert.That(first.Line, Is.GreaterThan(0), "строка обязательна");
            Assert.That(first.Column, Is.GreaterThan(0), "столбец обязателен");
            Assert.That(first.Hint, Is.Not.Null.And.Not.Empty, "без подсказки агент не исправится сам");
        });
    }

    [Test]
    public void Compile_UnknownType_HintsAboutReferences()
    {
        const string code = @"
public static class Workflow
{
    public static object Build()
    {
        Regex pattern = null;
        return pattern;
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.That(result.Ok, Is.False);

        RoslynDiagnostic first = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Id, Is.EqualTo("CS0246"));
            Assert.That(first.Hint, Does.Contain("using"), "подсказка объясняет, что добавить");
        });
    }

    [Test]
    public void Compile_WarningIsReturnedButDoesNotBlockAssembly()
    {
        const string code = @"
using System.Activities;
using System.Activities.Statements;

public static class Workflow
{
    public static Activity Build()
    {
        int unused = 5;
        return new Sequence();
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(result.Warnings, Is.Not.Empty, "предупреждения возвращаются агенту, но не блокируют процесс");
            Assert.That(result.Warnings[0], Does.Contain("CS0219"));
        });
    }

    [Test]
    public void Compile_TooLongCode_IsRejected()
    {
        RoslynSandboxOptions options = RoslynSandboxOptions.CreateDefault();
        options.MaxCodeLength = 20;

        RoslynCompileResult result = new RoslynSandbox(options).Compile(SimpleCode);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("code.too_long"));
        });
    }

    [Test]
    public void Compile_TimeoutIsReportedInsteadOfHanging()
    {
        RoslynSandboxOptions options = RoslynSandboxOptions.CreateDefault();
        options.CompileTimeout = TimeSpan.FromMilliseconds(1);

        var code = new StringBuilder();
        for (int index = 0; index < 1200; index++)
        {
            code.AppendLine($"public class Generated{index} {{ public int Value() {{ return {index}; }} }}");
        }

        RoslynCompileResult result = new RoslynSandbox(options).Compile(code.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("code.timeout"),
                "превышение лимита времени — ошибка инструмента, а не зависший вызов");
        });
    }

    [Test]
    public void Compile_EmptyCode_IsRejected()
    {
        RoslynCompileResult result = _sandbox.Compile("   ");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("code.empty"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("public static Activity"));
        });
    }

    [Test]
    public void Compile_BannedTextInComment_IsNotRejected()
    {
        const string code = @"
using System.Activities;
using System.Activities.Statements;

public static class Workflow
{
    // System.IO.File и System.Net здесь только в комментарии.
    public static Activity Build()
    {
        return new Sequence();
    }
}
";

        RoslynCompileResult result = _sandbox.Compile(code);

        Assert.That(result.Ok, Is.True,
            "проверка идёт по синтаксическому дереву, а комментарий — не код:" + Environment.NewLine + result.Describe());
    }
}
