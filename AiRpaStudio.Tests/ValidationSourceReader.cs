using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Чтение исходников студии для страж-тестов: правило, записанное словами в документации,
/// проверяется по коду, иначе оно откатывается при следующей правке.
/// </summary>
internal static class ValidationSourceReader
{
    /// <summary>Исходник по пути относительно корня репозитория.</summary>
    internal static string Read(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    /// <summary>Корень репозитория (по наличию <c>AiRpa.sln</c>).</summary>
    internal static string RepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
