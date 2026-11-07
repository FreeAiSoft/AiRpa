using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Правила состояния пакета в панели NuGet (docs\NuGet_Panel_Redesign_Plan.md §3): какое действие
/// предлагает большая кнопка, как подписывается версия и количество скачиваний.
/// <para>
/// Логика специально вынесена в <see cref="NuGetPackageStateResolver"/> без WPF и сети — здесь она
/// проверяется целиком, включая граничные случаи (пакет не подключён, выбранная версия ниже/выше,
/// совпадает с подключённой, неразбираемые версии).
/// </para>
/// </summary>
[TestFixture]
public class NuGetPackageStateTests
{
    [Test]
    public void ResolveAction_NoInstalledVersion_IsInstall()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageStateResolver.ResolveAction("13.0.4", null), Is.EqualTo(NuGetPackageAction.Install));
            Assert.That(NuGetPackageStateResolver.ResolveAction("13.0.4", string.Empty), Is.EqualTo(NuGetPackageAction.Install));
        });
    }

    [Test]
    public void ResolveAction_HigherTarget_IsUpdate()
    {
        Assert.That(NuGetPackageStateResolver.ResolveAction("10.0.14", "10.0.0"), Is.EqualTo(NuGetPackageAction.Update));
    }

    [Test]
    public void ResolveAction_LowerTarget_IsDowngrade()
    {
        Assert.That(NuGetPackageStateResolver.ResolveAction("9.0.4", "10.0.14"), Is.EqualTo(NuGetPackageAction.Downgrade));
    }

    [Test]
    public void ResolveAction_CurrentVersion_IsUninstall()
    {
        Assert.That(NuGetPackageStateResolver.ResolveAction("10.0.14", "10.0.14"), Is.EqualTo(NuGetPackageAction.Uninstall));
    }

    [Test]
    public void ResolveAction_EmptyTarget_IsNone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageStateResolver.ResolveAction(null, "1.0.0"), Is.EqualTo(NuGetPackageAction.None));
            Assert.That(NuGetPackageStateResolver.ResolveAction("  ", "1.0.0"), Is.EqualTo(NuGetPackageAction.None));
        });
    }

    [Test]
    public void ResolveAction_UnparsableVersions_FallBackToStringComparison()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageStateResolver.ResolveAction("latest", "1.0.0"), Is.EqualTo(NuGetPackageAction.Update));
            Assert.That(NuGetPackageStateResolver.ResolveAction("latest", "LATEST"), Is.EqualTo(NuGetPackageAction.Uninstall));
        });
    }

    [Test]
    public void ActionText_MatchesVisualStudioLabels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageStateResolver.ActionText(NuGetPackageAction.Install), Is.EqualTo("Install"));
            Assert.That(NuGetPackageStateResolver.ActionText(NuGetPackageAction.Update), Is.EqualTo("Update"));
            Assert.That(NuGetPackageStateResolver.ActionText(NuGetPackageAction.Downgrade), Is.EqualTo("Downgrade"));
            Assert.That(NuGetPackageStateResolver.ActionText(NuGetPackageAction.Uninstall), Is.EqualTo("Uninstall"));
        });
    }

    [Test]
    public void FormatDownloads_UsesShortSuffixes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageStateResolver.FormatDownloads(0), Is.Empty);
            Assert.That(NuGetPackageStateResolver.FormatDownloads(999), Is.EqualTo("999"));
            Assert.That(NuGetPackageStateResolver.FormatDownloads(12345), Is.EqualTo("12.3K"));
            Assert.That(NuGetPackageStateResolver.FormatDownloads(8360000000), Is.EqualTo("8.36B"));
        });
    }

    [Test]
    public void VersionLabel_MarksLatestAndInstalled()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NuGetPackageStateResolver.VersionLabel("10.0.14", true, false, false), Is.EqualTo("Latest stable 10.0.14"));
            Assert.That(NuGetPackageStateResolver.VersionLabel("11.0.0-beta", false, true, false), Is.EqualTo("Latest prerelease 11.0.0-beta"));
            Assert.That(NuGetPackageStateResolver.VersionLabel("9.0.0", false, false, true), Is.EqualTo("9.0.0 (installed)"));
            Assert.That(NuGetPackageStateResolver.VersionLabel("9.0.0", false, false, false), Is.EqualTo("9.0.0"));
        });
    }

    [Test]
    public void PackageItem_BuildsRowTextFromMetadata()
    {
        var item = new NuGetPackageItemViewModel(new NuGetPackageInfo
        {
            Id = "Microsoft.EntityFrameworkCore.SqlServer",
            Version = "10.0.14",
            Authors = "Microsoft",
            Description = "Microsoft SQL Server database provider for Entity Framework Core.",
            TotalDownloads = 8360000000
        });

        Assert.Multiple(() =>
        {
            Assert.That(item.AuthorLine, Is.EqualTo("by Microsoft"));
            Assert.That(item.DownloadsText, Is.EqualTo("8.36B downloads"));
            Assert.That(item.Monogram, Is.EqualTo("M"));
            Assert.That(item.IsPopular, Is.True, "у пакета 8.36B скачиваний — звёздочка обязана быть");
            Assert.That(item.IsInstalled, Is.False, "версия из карточки не означает «подключён»");
            Assert.That(item.Version, Is.EqualTo("10.0.14"));
        });
    }

    [Test]
    public void PackageItem_ReportsUpdateOnlyWhenInstalledDiffers()
    {
        var installed = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "A", Version = "1.0.0" })
        {
            InstalledVersion = "1.0.0",
            LatestVersion = "2.0.0"
        };

        var outdated = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "B", Version = "1.0.0" })
        {
            InstalledVersion = "1.0.0",
            LatestVersion = "1.0.0"
        };

        var notInstalled = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "C", Version = "1.0.0" })
        {
            LatestVersion = "2.0.0"
        };

        Assert.Multiple(() =>
        {
            Assert.That(installed.HasUpdate, Is.True);
            Assert.That(installed.StateText, Is.EqualTo("Update 2.0.0"));
            Assert.That(outdated.HasUpdate, Is.False);
            Assert.That(outdated.StateText, Is.EqualTo("Installed"));
            Assert.That(notInstalled.HasUpdate, Is.False);
            Assert.That(notInstalled.HasStateBadge, Is.False);
        });
    }

    [Test]
    public void Details_ActionFollowsSelectedVersionAndInstalledState()
    {
        var details = new NuGetPackageDetailsViewModel();
        var item = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "Newtonsoft.Json", Version = "13.0.4" });

        var catalog = new NuGetPackageDetails
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            Description = "Json.NET is a popular high-performance JSON framework for .NET",
            Authors = "James Newton-King",
            ProjectUrl = "https://www.newtonsoft.com/json"
        };
        catalog.Versions.Add(new NuGetPackageVersionInfo { Version = "13.0.4", Label = "Latest stable 13.0.4", IsLatestStable = true });
        catalog.Versions.Add(new NuGetPackageVersionInfo { Version = "12.0.3", Label = "12.0.3" });

        details.Load(catalog, item, installedVersion: null, sourceName: "nuget.org");

        Assert.Multiple(() =>
        {
            Assert.That(details.HasDetails, Is.True);
            Assert.That(details.SourceName, Is.EqualTo("nuget.org"));
            Assert.That(details.SelectedVersion.Version, Is.EqualTo("13.0.4"), "по умолчанию выбирается последняя стабильная версия");
            Assert.That(details.Action, Is.EqualTo(NuGetPackageAction.Install));
            Assert.That(details.ActionText, Is.EqualTo("Install"));
            Assert.That(details.ProjectUrl, Is.EqualTo("https://www.newtonsoft.com/json"));
        });

        details.Load(catalog, item, installedVersion: "12.0.3", sourceName: "nuget.org");

        Assert.Multiple(() =>
        {
            Assert.That(details.SelectedVersion.Version, Is.EqualTo("12.0.3"), "у подключённого пакета выбирается установленная версия");
            Assert.That(details.Action, Is.EqualTo(NuGetPackageAction.Uninstall));
        });

        details.SelectedVersion = details.Versions.First(v => v.Version == "13.0.4");

        Assert.Multiple(() =>
        {
            Assert.That(details.Action, Is.EqualTo(NuGetPackageAction.Update));
            Assert.That(details.PackageVersionText, Is.EqualTo("13.0.4"));
        });
    }

    [Test]
    public void Details_ReadmeTabsAreMutuallyExclusive()
    {
        var details = new NuGetPackageDetailsViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(details.IsReadmeSelected, Is.True, "как в VS: сначала открыт README");
            Assert.That(details.IsPackageDetailsSelected, Is.False);
        });

        details.IsPackageDetailsSelected = true;

        Assert.Multiple(() =>
        {
            Assert.That(details.IsPackageDetailsSelected, Is.True);
            Assert.That(details.IsReadmeSelected, Is.False);
        });

        details.IsReadmeSelected = true;

        Assert.Multiple(() =>
        {
            Assert.That(details.IsReadmeSelected, Is.True);
            Assert.That(details.IsPackageDetailsSelected, Is.False);
        });
    }

    [Test]
    public void Details_WithoutReadme_HidesReadmeTab()
    {
        var details = new NuGetPackageDetailsViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(details.ShowReadmeTab, Is.False, "вкладки README без README быть не должно");
            Assert.That(details.IsReadmeVisible, Is.False);
            Assert.That(details.IsPackageDetailsVisible, Is.True, "содержимое деталей видно сразу");
        });

        details.Readme = "# Заголовок";

        Assert.Multiple(() =>
        {
            Assert.That(details.HasReadme, Is.True);
            Assert.That(details.ShowReadmeTab, Is.True, "README появился — вкладка тоже");
            Assert.That(details.IsReadmeVisible, Is.True);
            Assert.That(details.IsPackageDetailsVisible, Is.False);
        });

        details.Readme = null;

        Assert.Multiple(() =>
        {
            Assert.That(details.ShowReadmeTab, Is.False, "README не загрузился — вкладка убирается");
            Assert.That(details.IsPackageDetailsVisible, Is.True, "показываются детали пакета");
        });
    }

    [Test]
    public void PreliminaryDetails_GiveVersionAndActiveActionWithoutSource()
    {
        // Панель показывает карточку из списка сразу, не дожидаясь ответа источника: иначе
        // ComboBox версий пустой, а кнопка действия недоступна (замечание по правой колонке).
        var info = new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            Version = "13.0.4",
            Authors = "James Newton-King",
            Description = "Json.NET is a popular high-performance JSON framework for .NET"
        };

        NuGetPackageDetails prelim = NuGetPackageDetailsService.FromPackageInfo(info, null);

        Assert.That(prelim, Is.Not.Null);
        Assert.That(prelim.Version, Is.EqualTo("13.0.4"));
        Assert.That(prelim.Versions, Has.Count.EqualTo(1));

        var details = new NuGetPackageDetailsViewModel();
        details.Load(prelim, new NuGetPackageItemViewModel(info), installedVersion: null, sourceName: "nuget.org");

        Assert.Multiple(() =>
        {
            Assert.That(details.SelectedVersion, Is.Not.Null, "версия выбрана сразу — ComboBox не пустой");
            Assert.That(details.SelectedVersion.Version, Is.EqualTo("13.0.4"));
            Assert.That(details.Action, Is.EqualTo(NuGetPackageAction.Install));
            Assert.That(details.ActionText, Is.EqualTo("Install"));
            Assert.That(details.PackageVersionText, Is.EqualTo("13.0.4"));
            Assert.That(details.HasDescription, Is.True);
        });
    }

    [Test]
    public void PreliminaryDetails_OfInstalledPackage_OfferUninstall()
    {
        var info = new NuGetPackageInfo { Id = "Newtonsoft.Json", Version = "13.0.4" };

        NuGetPackageDetails prelim = NuGetPackageDetailsService.FromPackageInfo(info, "12.0.3");
        var details = new NuGetPackageDetailsViewModel();
        details.Load(prelim, new NuGetPackageItemViewModel(info), installedVersion: "12.0.3", sourceName: "nuget.org");

        Assert.Multiple(() =>
        {
            Assert.That(details.SelectedVersion.Version, Is.EqualTo("12.0.3"), "выбрана версия из проекта");
            Assert.That(details.Action, Is.EqualTo(NuGetPackageAction.Uninstall));
        });
    }

    [Test]
    public void PackageItem_RowButtonsFollowState()
    {
        var notInstalled = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "A", Version = "1.0.0" });
        var outdated = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "B", Version = "1.0.0" })
        {
            InstalledVersion = "1.0.0",
            LatestVersion = "2.0.0"
        };
        var actual = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "C", Version = "1.0.0" })
        {
            InstalledVersion = "1.0.0",
            LatestVersion = "1.0.0"
        };

        Assert.Multiple(() =>
        {
            Assert.That(notInstalled.RowActionText, Is.EqualTo("Install"));
            Assert.That(notInstalled.CanInstallFromRow, Is.True);
            Assert.That(outdated.RowActionText, Is.EqualTo("Update"));
            Assert.That(outdated.CanInstallFromRow, Is.True);
            Assert.That(actual.CanInstallFromRow, Is.False, "актуальному пакету кнопка установки не нужна");
        });
    }

    [Test]
    public async Task PackageItem_LoadsIconAfterMetadataArrives()
    {
        // Иконка обязательно должна появиться: адрес приходит позже (из метаданных источника),
        // поэтому первый неудачный заход не имеет права «съесть» попытку.
        int calls = 0;
        var item = new NuGetPackageItemViewModel(
            new NuGetPackageInfo { Id = "Newtonsoft.Json" },
            _ =>
            {
                calls++;
                return Task.FromResult<ImageSource>(null);
            });

        await item.LoadIconAsync();
        Assert.That(calls, Is.Zero, "без адреса иконки запрос не выполняется");

        item.ApplyMetadata(new NuGetPackageInfo
        {
            Id = "Newtonsoft.Json",
            IconUrl = "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/icon.png"
        });
        await item.LoadIconAsync();

        Assert.That(calls, Is.EqualTo(1), "после прихода IconUrl иконка загружается");
    }

    [Test]
    public async Task Details_HeaderShowsIconOrMonogramFallback()
    {
        // Замечание приёмки: в правой колонке не было картинки пакета. Причина — шапка деталей
        // привязывалась к Item.Icon напрямую, а Item присваивался без уведомления (иконка вдобавок
        // приходит позже вызова LoadIconAsync). Проверяем фолбэк и приход картинки.
        var item = new NuGetPackageItemViewModel(
            new NuGetPackageInfo { Id = "Newtonsoft.Json", Version = "13.0.4" },
            _ => Task.FromResult<ImageSource>(new DrawingImage()));

        var details = new NuGetPackageDetailsViewModel();
        var changed = new System.Collections.Generic.List<string>();
        details.PropertyChanged += (sender, args) => changed.Add(args.PropertyName);

        details.Load(new NuGetPackageDetails { Id = "Newtonsoft.Json", Version = "13.0.4" }, item, null, "nuget.org");

        Assert.Multiple(() =>
        {
            Assert.That(details.PackageIcon, Is.Null, "иконки ещё нет — шапка показывает монограмм");
            Assert.That(details.HasPackageIcon, Is.False);
            Assert.That(details.PackageMonogram, Is.EqualTo("N"), "фолбэк — первая буква идентификатора");
        });

        await item.LoadIconAsync();

        Assert.Multiple(() =>
        {
            Assert.That(details.PackageIcon, Is.Not.Null, "картинка из карточки доходит до шапки деталей");
            Assert.That(details.HasPackageIcon, Is.True);
            Assert.That(changed, Does.Contain(nameof(NuGetPackageDetailsViewModel.PackageIcon)),
                "без уведомления привязка остаётся пустой — это и был дефект правой колонки");
        });
    }
}
