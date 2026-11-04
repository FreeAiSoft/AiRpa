using System;
using System.Collections.Generic;
using System.IO;
using Agent.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи приоритета каталогов промптов после переименования сборки ядра.
/// Промпты ищутся сначала рядом со сборкой (они едут в VSIX),
/// затем в пользовательском каталоге. Пользовательский каталог сменил имя, поэтому старый
/// обязан остаться только последним фолбэком на чтение: иначе у существующей установки
/// пропадут свои промпты, а точечные тесты этого не заметят.
/// </summary>
public class PromptPathsCandidatesTests
{
    [Test]
    public void EnumerateCandidates_ChecksAgentFolderBeforeLegacyFolder()
    {
        IReadOnlyList<string> candidates = PromptPaths.EnumerateCandidates();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string current = Path.Combine(appData, PromptPaths.AppDataFolderName, PromptPaths.PromptsFolderName);
        string legacy = Path.Combine(appData, PromptPaths.LegacyAppDataFolderName, PromptPaths.PromptsFolderName);

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(4),
                "кандидатов ровно четыре: рядом со сборкой, рядом с entry-сборкой, %AppData%/Agent, легаси-каталог");
            Assert.That(candidates[2], Is.EqualTo(current), "новый пользовательский каталог идёт третьим");
            Assert.That(candidates[3], Is.EqualTo(legacy), "легаси-каталог идёт последним");
            Assert.That(PromptPaths.LegacyAppDataFolderName, Is.Not.EqualTo(PromptPaths.AppDataFolderName),
                "легаси-константа обязана отличаться от текущего имени каталога");
        });
    }

    [Test]
    public void EnumerateCandidates_StartsWithAssemblyAdjacentPromptsDirectory()
    {
        string first = PromptPaths.EnumerateCandidates()[0];
        string coreDir = Path.GetDirectoryName(typeof(PromptPaths).Assembly.Location);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Path.Combine(coreDir, PromptPaths.PromptsFolderName)),
                "первым проверяется каталог рядом со сборкой ядра: промпты поставляются вместе с VSIX");
            Assert.That(PromptPaths.AppDataFolderName, Is.EqualTo("Agent"),
                "пользовательский каталог переименован вместе со сборкой");
        });
    }

    [Test]
    public void ResolvePromptsDirectory_ReturnsNonEmptyPath()
    {
        Assert.That(PromptPaths.ResolvePromptsDirectory(), Is.Not.Empty);
    }
}