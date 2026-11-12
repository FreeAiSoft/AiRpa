using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Источники NuGet: чтение внешних конфигов (машина/решение), импорт в per-project хранилище и
/// явные действия с NuGet.Config (создание/экспорт). Все тесты без сети и без записи в реальные
/// конфиги пользователя — работают во временных каталогах.
/// </summary>
[TestFixture]
public class NuGetSourcesConfigTests
{
    [Test]
    public void LoadSolutionSources_ReadsConfigWithoutWriting()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"corp\" value=\"https://corp.example.org/v3/index.json\" />"
                + "</packageSources></configuration>");

            var service = new NuGetSourcesService();
            IReadOnlyList<NuGetSourceDefinition> sources = service.LoadSolutionSources(directory);

            Assert.Multiple(() =>
            {
                Assert.That(sources.Select(source => source.Name), Does.Contain("corp"),
                    "источник уровня решения читается из NuGet.Config");
                Assert.That(sources.Single(source => source.Name == "corp").Origin, Is.EqualTo(NuGetSourceOrigin.Solution));
                Assert.That(sources.Single(source => source.Name == "corp").IsReadOnly, Is.True);
                Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1),
                    "чтение решения не должно создавать файлы в каталоге проекта");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void WriteConfig_WritesEnabledAndDisabledSources()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "NuGet.Config");
            var service = new NuGetSourcesService();
            service.WriteConfig(path, new[]
            {
                new NuGetSourceDefinition { Name = "corp", Url = "https://corp.example.org/v3/index.json", IsEnabled = true },
                new NuGetSourceDefinition { Name = "off", Url = "https://off.example.org/v3/index.json", IsEnabled = false },
            });

            string config = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(config, Does.Contain("corp"));
                Assert.That(config, Does.Contain("off"));
                Assert.That(config, Does.Contain("disabledPackageSources"));
                Assert.That(config, Does.Contain("value=\"true\""), "выключенный источник попадает в disabledPackageSources");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void OpenSources_LoadsExternalSourcesFromSolutionConfig()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"corp\" value=\"https://corp.example.org/v3/index.json\" />"
                + "</packageSources></configuration>");

            var model = new NuGetPanelViewModel();
            LoadProject(model, directory);

            model.OpenSourcesCommand.Execute(null);

            Assert.That(model.ExternalSources.Select(source => source.Name), Does.Contain("corp"),
                "внешние источники решения видны в оверлее");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ImportSource_AddsExternalSourceToProject()
    {
        string directory = CreateTempDirectory();
        try
        {
            var model = new NuGetPanelViewModel();
            InMemoryStore store = LoadProject(model, directory);

            var external = new NuGetSourceDefinition { Name = "corp", Url = "https://corp.example.org/v3/index.json", IsEnabled = true };
            model.ExternalSources.Add(external);
            model.SelectedExternalSource = external;

            model.ImportSourceCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(model.SourceItems.Select(item => item.Name), Does.Contain("corp"),
                    "внешний источник импортирован в источники проекта");
                Assert.That(store.Items.Select(item => item.Name), Does.Contain("corp"),
                    "импорт персистится через хук SaveProjectSources");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ExportProjectSources_WritesNuGetConfig()
    {
        string directory = CreateTempDirectory();
        try
        {
            var model = new NuGetPanelViewModel();
            LoadProject(model, directory, new[]
            {
                new NuGetSourceDefinition { Name = "corp", Url = "https://corp.example.org/v3/index.json", IsEnabled = true }
            });

            model.ExportProjectSourcesToConfigCommand.Execute(null);

            string configPath = Path.Combine(directory, "NuGet.Config");
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(configPath), Is.True, "экспорт создаёт NuGet.Config в каталоге проекта");
                Assert.That(File.ReadAllText(configPath), Does.Contain("corp"),
                    "источник проекта записан в NuGet.Config");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void CreateNuGetConfig_CreatesOnlyOnce()
    {
        string directory = CreateTempDirectory();
        try
        {
            var model = new NuGetPanelViewModel();
            LoadProject(model, directory, new[]
            {
                new NuGetSourceDefinition { Name = "corp", Url = "https://corp.example.org/v3/index.json", IsEnabled = true }
            });

            model.CreateNuGetConfigCommand.Execute(null);
            model.CreateNuGetConfigCommand.Execute(null);

            Assert.That(File.Exists(Path.Combine(directory, "NuGet.Config")), Is.True);
            Assert.That(model.StatusText, Does.Contain("already exists"),
                "повторное создание не перезаписывает существующий файл");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // --- служебное ---

    private static InMemoryStore LoadProject(NuGetPanelViewModel model, string directory, IEnumerable<NuGetSourceDefinition> sources = null)
    {
        string project = Path.Combine(directory, "Demo.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup /></Project>");

        var store = new InMemoryStore(sources);
        model.LoadProjectSources = store.Load;
        model.SaveProjectSources = store.Save;
        Assert.That(model.LoadProject(project), Is.True);
        return store;
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nuget-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class InMemoryStore
    {
        private List<NuGetSourceDefinition> _sources;

        public InMemoryStore(IEnumerable<NuGetSourceDefinition> sources)
        {
            _sources = (sources ?? Enumerable.Empty<NuGetSourceDefinition>()).ToList();
        }

        public IReadOnlyList<NuGetSourceDefinition> Load() => _sources.ToList();

        public void Save(IReadOnlyList<NuGetSourceDefinition> sources) => _sources = sources.ToList();

        public IReadOnlyList<NuGetSourceDefinition> Items => _sources;
    }
}
