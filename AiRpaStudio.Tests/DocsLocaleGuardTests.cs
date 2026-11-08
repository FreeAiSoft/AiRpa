using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Страж структуры встроенной пользовательской документации.
/// <para>
/// Правило: пользовательская документация (`AiRpaStudio\Docs`) хранится строго по локалям —
/// в корне каталога `Docs` должны находиться только папки `ru` и `en`, а каждый `.md` обязан
/// лежать внутри `ru/` или `en/`. Никаких `.md` (и вообще файлов) в корне `Docs` быть не может:
/// их подхватил бы `HelpDocumentation.ReadAll()`/`DocumentationIndex.Build()` как «документацию без
/// локали», и она попадала бы в выдачу чата вне зависимости от выбранного языка.
/// </para>
/// <para>
/// Дополнительно страж держит главную страницу русской справки `ru\index.md`: она обязана
/// существовать и ссылаться на каждый документ-фичу в `ru/`, а список ссылок обязан совпадать
/// с фактическим набором `.md` в `ru/` (без потерянных и без битых ссылок).
/// </para>
/// </summary>
[TestFixture]
public class DocsLocaleGuardTests
{
    private const string DocsRelative = @"AiRpaStudio\Docs";
    private const string RuMainPage = "index.md";

    [Test]
    public void DocsDirectory_ContainsOnlyLocaleFolders()
    {
        string docs = DocsDirectory();

        Assert.That(Directory.Exists(docs), Is.True,
            "каталог пользовательской документации не найден: " + docs);

        string[] entries = Directory.GetFileSystemEntries(docs);
        var offenders = entries
            .Select(e => Relative(docs, e))
            .Where(name =>
            {
                string full = IOPath.Combine(docs, name);
                bool isLocaleDir = (name == "ru" || name == "en")
                    && Directory.Exists(full);
                return !isLocaleDir;
            })
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "в корне Docs должны лежать только папки локалей `ru` и `en`; найден лишний контент:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void AllUserDocs_AreUnderRuOrEn()
    {
        string docs = DocsDirectory();

        Assert.That(Directory.Exists(docs), Is.True,
            "каталог пользовательской документации не найден: " + docs);

        var offenders = Directory
            .GetFiles(docs, "*.md", SearchOption.AllDirectories)
            .Select(f => Relative(docs, f))
            .Where(rel => !rel.StartsWith("ru" + IOPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                       && !rel.StartsWith("en" + IOPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(rel, "ru", StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(rel, "en", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "каждый .md пользовательской документации обязан лежать в `ru/` или `en/`; файлы вне локалей:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void RuMainPage_ExistsAndLinksEveryRuFeatureDocument()
    {
        string ru = IOPath.Combine(DocsDirectory(), "ru");
        string main = IOPath.Combine(ru, RuMainPage);

        Assert.That(File.Exists(main), Is.True,
            "главная страница русской справки обязана существовать: " + main);

        string content = File.ReadAllText(main);

        string[] featureDocs = Directory
            .GetFiles(ru, "*.md", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(IOPath.GetFileName(f), RuMainPage, StringComparison.OrdinalIgnoreCase))
            .Select(f => IOPath.GetFileName(f))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(featureDocs, Is.Not.Empty,
            "в ru/ должны лежать документы-фичи, на которые ссылается главная страница");

        HashSet<string> referenced = ReferencedMdFiles(content);

        var missing = featureDocs.Where(f => !referenced.Contains(f)).ToList();
        Assert.That(missing, Is.Empty,
            "главная страница " + main + " не ссылается на документы-фичи:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));

        var broken = referenced
            .Where(f => !featureDocs.Contains(f, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.That(broken, Is.Empty,
            "главная страница ссылается на .md, которых нет в ru/:"
            + Environment.NewLine + string.Join(Environment.NewLine, broken));
    }

    private static HashSet<string> ReferencedMdFiles(string content)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Markdown-ссылки вида [текст](target.md)
        foreach (Match m in Regex.Matches(content, @"\]\(([^)#]+)\)"))
        {
            string target = m.Groups[1].Value.Trim();
            if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                result.Add(IOPath.GetFileName(target));
        }

        return result;
    }

    private static string DocsDirectory()
    {
        return IOPath.Combine(FindRepositoryRoot(), DocsRelative);
    }

    private static string Relative(string root, string path)
    {
        return path.Substring(root.Length).TrimStart(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
