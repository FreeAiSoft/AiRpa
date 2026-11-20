using System;
using System.IO;
using System.Linq;
using Agent.Core.Models;
using Agent.Core.Services;
using RPA.Chat.Services;
using RPA.Shared.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи интеграции нового чата: MainView кладёт в док ChatHostView (а не ChatPanel),
/// StudioChatHost реализует IChatHost, тема идёт через AgentThemeManager, ChatEnvironment
/// по-прежнему заполняется в MainView, а RPA.Chat не реализует голосовой контракт тулкита.
/// </summary>
public class ChatIntegrationGuardTests
{
    private string _directory;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AiRpaStudio.ChatIntegration." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        ChatPaths.OverrideDataDirectory(_directory);
        RpaAgentOptions.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        ChatPaths.OverrideDataDirectory(null);
        RpaAgentOptions.Reset();
    }

    // ------------------------------------------------------------------
    // Источниковая интеграция
    // ------------------------------------------------------------------

    [Test]
    public void MainView_UsesChatHostView_NotChatPanel()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("new Chat.Views.ChatPanel()"),
                "в док панели Chat больше не должно создаваться ChatPanel");
            Assert.That(code, Does.Contain("new Chat.Views.ChatHostView()"),
                "MainView должен создавать новый контрол ChatHostView");
            Assert.That(code, Does.Contain("private Chat.Views.ChatHostView chatPanel;"),
                "поле chatPanel должно иметь тип ChatHostView");
        });
    }

    [Test]
    public void StudioChatHost_ImplementsIChatHost()
    {
        string code = Read(@"RPA.Chat\Services\StudioChatHost.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("class StudioChatHost : IChatHost"),
                "StudioChatHost обязан реализовывать Agent.Toolkit.Wpf.Controls.IChatHost");
            Assert.That(code, Does.Contain("Host = _host"),
                "ChatControl.Host должен получать реализацию IChatHost");
        });
    }

    [Test]
    public void MainView_ThemeUsesAgentThemeManager_NotChatStudioBridge()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("ChatStudioBridge"),
                "мост ChatStudioBridge больше не используется");
            Assert.That(code, Does.Contain("AgentThemeManager.SetMode(AgentThemeMode.Auto)"),
                "тема тулкита ведётся через AgentThemeManager.SetMode(Auto)");
            Assert.That(code, Does.Contain("AgentThemeManager.Apply("),
                "тема тулкита транслируется через AgentThemeManager.Apply");
        });
    }

    [Test]
    public void MainView_StillFillsChatEnvironment()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ChatEnvironment.SolutionPathProvider"));
            Assert.That(code, Does.Contain("ChatEnvironment.ActivityCatalog"));
            Assert.That(code, Does.Contain("ChatEnvironment.WorkflowOutline"));
            Assert.That(code, Does.Contain("ChatEnvironment.WorkflowEditing"));
            Assert.That(code, Does.Contain("ChatEnvironment.WorkflowXamlGate"));
            Assert.That(code, Does.Contain("ChatEnvironment.WorkflowMcpBridge"));
            Assert.That(code, Does.Contain("ChatEnvironment.ActivityPaletteNotifier"));
            Assert.That(code, Does.Contain("ChatEnvironment.WorkflowRun"));
        });
    }

    [Test]
    public void RpaChat_DoesNotImplementToolkitMicrophoneContract()
    {
        string root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "RPA.Chat"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("IMicrophoneRecorder"))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "RPA.Chat не должен реализовывать IMicrophoneRecorder: голос поставляет внешний проект: "
            + string.Join(", ", offenders));
    }

    // ------------------------------------------------------------------
    // Политики разрешений / способов WF (канонический SettingsData)
    // ------------------------------------------------------------------

    [Test]
    public void WorkflowWritePolicy_Resolve_NullSettings_ReturnsNone()
    {
        Assert.That(WorkflowWritePolicy.Resolve(null), Is.EqualTo(WorkflowWriteWay.None));
    }

    [Test]
    public void WorkflowWritePolicy_Resolve_EachFlag_MapsToSingleWay()
    {
        Assert.Multiple(() =>
        {
            RpaAgentOptions.Reset();
            RpaAgentOptions.AllowWfPalette = true;
            RpaAgentOptions.AllowWfJsonAst = false;
            RpaAgentOptions.AllowWfCodeFirst = false;
            Assert.That(WorkflowWritePolicy.Resolve(new SettingsData()), Is.EqualTo(WorkflowWriteWay.Palette));

            RpaAgentOptions.Reset();
            RpaAgentOptions.AllowWfPalette = false;
            RpaAgentOptions.AllowWfJsonAst = true;
            RpaAgentOptions.AllowWfCodeFirst = false;
            Assert.That(WorkflowWritePolicy.Resolve(new SettingsData()), Is.EqualTo(WorkflowWriteWay.JsonAst));

            RpaAgentOptions.Reset();
            RpaAgentOptions.AllowWfPalette = false;
            RpaAgentOptions.AllowWfJsonAst = false;
            RpaAgentOptions.AllowWfCodeFirst = true;
            Assert.That(WorkflowWritePolicy.Resolve(new SettingsData()), Is.EqualTo(WorkflowWriteWay.CodeFirst));
        });
    }

    [Test]
    public void WorkflowWritePolicy_Resolve_DefaultOptions_ReturnsAll()
    {
        RpaAgentOptions.Reset();
        Assert.That(WorkflowWritePolicy.Resolve(new SettingsData()), Is.EqualTo(WorkflowWriteWay.All));
    }

    [Test]
    public void AgentPermissionsPolicy_DefaultSettings_AllowReadAndEditOnly()
    {
        var allowed = AgentPermissionsPolicy.Resolve(new SettingsData());
        Assert.That(allowed, Is.EqualTo(AgentPermissions.ReadFiles | AgentPermissions.EditFiles));
    }

    [Test]
    public void AgentPermissionsPolicy_NullSettings_DoesNotThrow()
    {
        Assert.That(AgentPermissionsPolicy.Resolve(null), Is.EqualTo((AgentPermissions)0));
    }

    // ------------------------------------------------------------------
    // Миграция настроек: Theme/UseDarkTheme → BaseTheme
    // ------------------------------------------------------------------

    [Test]
    public void ChatSettingsStore_LegacyTheme_MigratesToBaseTheme()
    {
        File.WriteAllText(ChatPaths.SettingsFile, "{\"Theme\":\"Light\",\"UseDarkTheme\":null}");

        SettingsData settings = new ChatSettingsStore().Load();

        Assert.That(settings.BaseTheme, Is.EqualTo("Light"),
            "старое поле Theme=\"Light\" должно переноситься в канонический BaseTheme");
    }

    [Test]
    public void ChatSettingsStore_LegacyUseDarkThemeFalse_MigratesToLight()
    {
        File.WriteAllText(ChatPaths.SettingsFile, "{\"UseDarkTheme\":false}");

        SettingsData settings = new ChatSettingsStore().Load();

        Assert.That(settings.BaseTheme, Is.EqualTo("Light"),
            "старый формат {\"UseDarkTheme\":false} должен переноситься в BaseTheme=\"Light\"");
    }

    [Test]
    public void ChatSettingsStore_MissingTheme_DefaultsToDark()
    {
        File.WriteAllText(ChatPaths.SettingsFile, "{}");

        SettingsData settings = new ChatSettingsStore().Load();

        Assert.That(settings.BaseTheme, Is.EqualTo("Dark"),
            "отсутствующая тема должна давать дефолт студии (Dark)");
    }

    // ------------------------------------------------------------------
    // SettingsPanel (Шаг 2 плана AgentTabControl)
    // ------------------------------------------------------------------

    [Test]
    public void ChatHostView_WiresSettingsPanelWithSettingsModelAndAutoTheme()
    {
        string host = Read(@"RPA.Chat\Views\ChatHostView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(host, Does.Contain("_settingsModel = new SettingsModel(_settingsStore, secretsStore: _secretsStore)"),
                "SettingsPanel должна получать SettingsModel с ISettingsStore/ISecretsStore");
            Assert.That(host, Does.Contain("_settingsModel.ThemeMode = \"Auto\""),
                "SettingsControl должен работать в режиме Auto (тема принадлежит хосту студии)");
            Assert.That(host, Does.Contain("_mainTabs.SettingsPanel.DataContext = new SettingsPanelViewModel(_settingsModel)"),
                "DataContext вкладки Settings должен выставлять VM с SettingsModel + Cmd");
        });
    }

    [Test]
    public void SettingsPanelViewModel_ExposesSettingsModelAndInheritsCmd()
    {
        string vm = Read(@"RPA.Chat\ViewModels\SettingsPanelViewModel.cs");

        Assert.Multiple(() =>
        {
            Assert.That(vm, Does.Contain("class SettingsPanelViewModel : BaseViewModel"),
                "VM должна наследовать BaseViewModel, чтобы пробросить Cmd в SettingsControl");
            Assert.That(vm, Does.Contain("public SettingsModel Settings"),
                "SettingsControl читает DataContext.Settings как Agent.Models.SettingsModel");
        });
    }


    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (AiRpa.sln).");
    }
}
