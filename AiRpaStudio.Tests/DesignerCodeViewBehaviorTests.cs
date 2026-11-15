using System;
using System.IO;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поведенческая проверка переключения «дизайнер → редактор кода» на живом <see cref="RPA.Core.WorkflowDesignerEx"/>.
/// Сценарий выполняется в изолированном домене стенда (<see cref="DesignerBenchProxy"/>), потому что настоящий
/// конструктор заполняет статические кэши шрифтов WPF, которые нельзя смешивать с тестами темы и из-за которых при
/// выгрузке домена возникает <c>AccessViolation</c>.
/// <para>
/// Проверяется контракт, который раньше был нарушен: при «Открыть в редакторе кода» у XAML-активности, открытой в
/// дизайнере, открывалась вторая вкладка, а буфер дизайнера (и звёздочка «изменён») не переносились. Теперь
/// <c>Main.CmdOpenInCodeEditor</c> сериализует буфер и переносит его в <c>CodeDocument</c> той же вкладки, а дизайнер
/// отсоединяется от модели.
/// </para>
/// </summary>
[TestFixture]
public class DesignerCodeViewBehaviorTests
{
    [Test]
    public void CodeViewSwitch_TransfersDirtyDesignerBuffer()
    {
        string rpa1 = FindRpa1();
        string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

        DesignerCodeViewReport report = DesignerBenchProxy.Shared.CodeViewSwitchScenario(
            Path.Combine(rpa1, "Main.xaml"), xamlFiles);

        Assert.That(report.Error, Is.Null.Or.Empty,
            "сценарий переключения в код упал." + Environment.NewLine + report.Description);

        Assert.Multiple(() =>
        {
            Assert.That(report.LoadSucceeded, Is.True, report.Description);
            Assert.That(report.EditedAfterModelChange, Is.True,
                "реальная правка модели обязана пометить дизайнер «изменённым»." + Environment.NewLine + report.Description);
            Assert.That(report.XamlTextNonEmpty, Is.True,
                "Flush() обязан сериализовать буфер дизайнера в XAML." + Environment.NewLine + report.Description);
            Assert.That(report.DesignerDetached, Is.True,
                "дизайнер обязан быть отсоединён от модели при переключении в код." + Environment.NewLine + report.Description);
            Assert.That(report.CodeDocumentOpened, Is.True,
                "переключение в код обязано открыть CodeDocument того же файла." + Environment.NewLine + report.Description);
            Assert.That(report.DirtyCarriedOver, Is.True,
                "признак «изменён» переносится из дизайнера в редактор кода." + Environment.NewLine + report.Description);
            Assert.That(report.TextMatchesBuffer, Is.True,
                "в редактор попадает сериализованный буфер дизайнера, а не текст с диска." + Environment.NewLine + report.Description);
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