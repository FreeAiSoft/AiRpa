using System;
using System.IO;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поведенческая проверка переключения «редактор кода → дизайнер» на живом <see cref="RPA.Core.WorkflowDesignerEx"/>.
/// Сценарий выполняется в изолированном домене стенда (<see cref="DesignerBenchProxy"/>), как и
/// <see cref="DesignerCodeViewBehaviorTests"/>, из-за статических кэшей шрифтов WPF.
/// <para>
/// Проверяется контракт: когда XAML открыт в редакторе кода и его открывают как активность, дизайнер
/// грузится из несохранённого буфера (а не с диска), а признак «изменён» переносится в него.
/// </para>
/// </summary>
[TestFixture]
public class DesignerOverCodeBehaviorTests
{
    [Test]
    public void DesignerOverCode_LoadsFromBufferAndCarriesDirty()
    {
        string rpa1 = FindRpa1();
        string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

        DesignerOverCodeReport report = DesignerBenchProxy.Shared.CodeOverDesignerScenario(
            Path.Combine(rpa1, "Main.xaml"), xamlFiles);

        Assert.That(report.Error, Is.Null.Or.Empty,
            "сценарий переключения в дизайнер упал." + Environment.NewLine + report.Description);

        Assert.Multiple(() =>
        {
            Assert.That(report.BufferDiffersFromDisk, Is.True,
                "предусловие сценария: буфер обязан отличаться от файла на диске." + Environment.NewLine + report.Description);
            Assert.That(report.DesignerCreated, Is.True,
                "дизайнер обязан открыться из текста буфера." + Environment.NewLine + report.Description);
            Assert.That(report.LoadedFromBuffer, Is.True,
                "после Flush() дизайнер обязан отдавать содержимое буфера, а не файла на диске." + Environment.NewLine + report.Description);
            Assert.That(report.EditedCarriedOver, Is.True,
                "признак «изменён» переносится из редактора кода в дизайнер." + Environment.NewLine + report.Description);
        });
    }

    private static string FindRpa1()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("AiRpa.sln не найден.");
        }

        string rpa1 = Path.Combine(directory.FullName, "RPA-1");
        Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
        return rpa1;
    }
}
