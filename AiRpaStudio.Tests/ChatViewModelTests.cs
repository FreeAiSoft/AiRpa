using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RPA.Chat.Markdown;
using RPA.Chat.Services;
using RPA.Chat.ViewModels;
using Agent.Core.Models;

namespace AiRpaStudio.Tests;

public class ChatViewModelTests
{
    [SetUp]
    public void SetUp()
    {
        // Isolate the chat's JSON persistence (settings/history/ui-state) from the real profile.
        var temp = Path.Combine(Path.GetTempPath(), "AiRpaStudio.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        ChatPaths.OverrideDataDirectory(temp);
    }

    [TearDown]
    public void TearDown()
    {
        ChatPaths.OverrideDataDirectory(null);
    }

    [Test]
    public async Task SendAsync_WithoutApiKey_UsesStub_AppendsUserAndResult()
    {
        var vm = new ChatViewModel();
        vm.ModeName = "Plan"; // deterministic mode regardless of persisted ui state
        vm.InputText = "Привет";

        await vm.SendAsync();

        Assert.That(vm.Messages.Any(m => m.Kind == ChatMessageKind.User), Is.True, "user message should be appended");
        Assert.That(vm.Messages.Any(m => m.Kind == ChatMessageKind.ResultAnswerPlan), Is.True, "a plan result should be appended (default Plan mode)");
        Assert.That(vm.IsSending, Is.False);
    }

    [Test]
    public void ModeName_Change_SwitchesModeAndNotifies()
    {
        var vm = new ChatViewModel();
        vm.ModeName = "Plan"; // normalize to a known mode
        vm.ModeName = "Act";  // must switch and append a notice

        Assert.That(vm.Mode, Is.EqualTo(AgentMode.Act));
        Assert.That(vm.Messages.Any(m => m.Kind == ChatMessageKind.ModeSwitch), Is.True);
    }

    [Test]
    public void ModeItems_Exposes_Plan_Act_Help()
    {
        var vm = new ChatViewModel();

        Assert.That(vm.ModeItems.Select(i => i.Label).ToArray(),
            Is.EqualTo(new[] { "Plan", "Act", "Help" }));
    }

    [Test]
    public void NewChat_ResetsMessages()
    {
        var vm = new ChatViewModel();
        vm.AppendSystemMessage("hello");

        vm.NewChatCommand.Execute(null);

        Assert.That(vm.Messages, Is.Empty);
        Assert.That(vm.IsConversationView, Is.True);
    }

    [Test]
    public void ShowHistory_HidesPinnedMessage()
    {
        var vm = new ChatViewModel();
        vm.AppendSystemMessage("hello");

        Assert.That(vm.IsConversationView, Is.True);
        Assert.That(vm.IsPinnedMessageVisible, Is.True);

        vm.ShowHistoryCommand.Execute(null);

        Assert.That(vm.IsConversationView, Is.False);
        Assert.That(vm.IsPinnedMessageVisible, Is.False);

        vm.BackToChatCommand.Execute(null);

        Assert.That(vm.IsConversationView, Is.True);
        Assert.That(vm.IsPinnedMessageVisible, Is.True);
    }

    [Test]
    public void ClearInput_Executes_ClearsInputText()
    {
        var vm = new ChatViewModel();
        vm.InputText = "some text";

        vm.ClearInputCommand.Execute(null);

        Assert.That(vm.InputText, Is.EqualTo(""));
    }

    [Test]
    public void UpdateMemory_WithToolMode_SetsToolPrompt()
    {
        var vm = new ChatViewModel();
        vm.Settings.MemoryBankMode = "Tool";

        vm.UpdateMemoryCommand.Execute(null);

        Assert.That(vm.InputText, Does.Contain("read_memory"));
    }

    [Test]
    public void UpdateMemory_WithConventionMode_SetsConventionPrompt()
    {
        var vm = new ChatViewModel();
        vm.Settings.MemoryBankMode = "Convention";

        vm.UpdateMemoryCommand.Execute(null);

        Assert.That(vm.InputText, Does.Contain("read_files"));
    }

    [Test]
    public void RefreshPrompts_WithStubProvider_AppendsUnavailableNotice()
    {
        var vm = new ChatViewModel();

        vm.RefreshPromptsCommand.Execute(null);

        Assert.That(vm.Messages.Any(m => m.Text.Contains("unavailable")), Is.True);
    }
    [Test]
    public void DocsCommands_SetAndClearRelativeNavigator()
    {
        var vm = new ChatViewModel();

        vm.DocsHomeCommand.Execute(null);
        Assert.That(MarkdownRenderer.RelativeLinkNavigator, Is.Not.Null);

        vm.DocsCloseCommand.Execute(null);
        Assert.That(MarkdownRenderer.RelativeLinkNavigator, Is.Null);
    }

    // ---- AG-10: настройки → маска разрешений (одна точка правды, её же зовёт ChatViewModel) ----



    [Test]
    public void PermissionsPolicy_DefaultSettings_AllowReadAndEditOnly()
    {
        // Умолчания SettingsData: чтение и правка файлов включены, команды/сеть/подагенты — нет.
        var allowed = AgentPermissionsPolicy.Resolve(new SettingsData());

        Assert.That(allowed, Is.EqualTo(AgentPermissions.ReadFiles | AgentPermissions.EditFiles));
    }

    [Test]
    public void PermissionsPolicy_FollowsEverySwitch()
    {
        var settings = new SettingsData
        {
            AllowReadFiles = false,
            AllowEditFiles = true,
            AllowExecuteCommand = true,
            AllowWebFetch = true,
            AllowSubagents = true
        };

        var allowed = AgentPermissionsPolicy.Resolve(settings);

        Assert.Multiple(() =>
        {
            Assert.That(allowed, Is.EqualTo(
                AgentPermissions.EditFiles
                | AgentPermissions.ExecuteCommand
                | AgentPermissions.WebFetch
                | AgentPermissions.Subagents));
            Assert.That(allowed & AgentPermissions.ReadFiles, Is.EqualTo((AgentPermissions)0),
                "выключенный переключатель не должен оставлять категорию в маске");
        });
    }

    [Test]
    public void PermissionsPolicy_AllSwitchesOff_GivesEmptyMask()
    {
        var settings = new SettingsData
        {
            AllowReadFiles = false,
            AllowEditFiles = false,
            AllowExecuteCommand = false,
            AllowWebFetch = false,
            AllowSubagents = false
        };

        Assert.That(AgentPermissionsPolicy.Resolve(settings), Is.EqualTo((AgentPermissions)0));
    }

    [Test]
    public void PermissionsPolicy_NullSettings_DoesNotThrow()
    {
        // Null приходит из ранней инициализации: отвечаем «ничего не разрешено»,
        // чтобы отсутствие настроек не превращалось в полный доступ.
        Assert.That(AgentPermissionsPolicy.Resolve(null), Is.EqualTo((AgentPermissions)0));
    }

}
