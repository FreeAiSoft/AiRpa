using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

/// <summary>
/// Этап 4: при попадании в точку останова TrackHandler обновляет панель «Локальные переменные»
/// снимком выбранного источника (Tracking / ActivityInstance / Mixed).
/// </summary>
public class TrackHandlerLocalsTests
{
    [Test]
    public void TrackHandler_ResolvesLocalsProviderByMode()
    {
        string code = ReadSource(@"AiRpaStudio\Helpers\TrackHandler.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ILocalsProvider"), "контракт ILocalsProvider не используется");
            Assert.That(code, Does.Contain("LocalsProviderFactory"), "фабрика выбора источника не используется");
            Assert.That(code, Does.Contain("LocalsProviderMode.Mixed"), "смешанный режим не является фолбэком");
        });
    }

    [Test]
    public void ShowDebug_UpdatesLocalsModel_OnBreakpointHit()
    {
        string method = MethodBody("ShowDebug");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ActivityStateRecord record"), "ShowDebug не принимает трекинг-запись");
            Assert.That(method, Does.Contain("ResolveLocalsProvider().GetSnapshot(record)"), "провайдер не вызывается для снимка");
            Assert.That(method, Does.Contain("localsModel.Update("), "модель Locals не обновляется");
            Assert.That(method, Does.Contain("CustomWfDesigner.GetInstance().LocalsModel"), "модель берётся не из студии");
        });
    }

    [Test]
    public void ShowDebug_UpdatesLocals_OnUiDispatcher_BeforeWait()
    {
        string method = MethodBody("ShowDebug");

        int dispatcher = method.IndexOf("dispatcher.Invoke(", StringComparison.Ordinal);
        int update = method.IndexOf("localsModel.Update(", StringComparison.Ordinal);
        int wait = method.IndexOf("resumeRuntimeFromHost.WaitOne()", StringComparison.Ordinal);

        Assert.That(dispatcher, Is.GreaterThanOrEqualTo(0), "нет маршалинга на UI-поток");
        Assert.That(update, Is.GreaterThan(dispatcher), "обновление Locals должно идти на UI-потоке");
        Assert.That(wait, Is.GreaterThan(update), "обновление Locals должно происходить до блокировки WaitOne");
    }

    [Test]
    public void ShowDebug_SkipsLocalsAndWait_OnPrestart()
    {
        string method = MethodBody("ShowDebug");

        int guard = method.IndexOf("record == null", StringComparison.Ordinal);
        Assert.That(guard, Is.GreaterThanOrEqualTo(0), "нет защиты от синтетического Prestart (record == null)");

        int snapshot = method.IndexOf("ResolveLocalsProvider().GetSnapshot(record)", StringComparison.Ordinal);
        int wait = method.IndexOf("resumeRuntimeFromHost.WaitOne()", StringComparison.Ordinal);

        Assert.That(snapshot, Is.GreaterThan(guard), "снимок Locals не должен формироваться на Prestart");
        Assert.That(wait, Is.GreaterThan(guard), "остановка WaitOne не должна выполняться на Prestart");
    }

    [Test]
    public void CustomTrackingParticipant_PrestartBranch_DoesNotPassRecord()
    {
        string source = ReadSource(@"ActivitiesTracker\Core\CustomTrackingParticipant.cs");

        const string marker = "ActivityState = ActivityState.Prestart";
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), "нет Prestart-ветки в CustomTrackingParticipant");

        int start = source.LastIndexOf("new TrackingEventArgs()", markerIndex, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "нет инициализатора TrackingEventArgs для Prestart");

        int end = source.IndexOf("});", markerIndex, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), "не найден конец инициализатора Prestart");

        string initializer = source.Substring(start, end - start);

        Assert.Multiple(() =>
        {
            Assert.That(initializer, Does.Contain("ActivityState = ActivityState.Prestart"));
            Assert.That(initializer, Does.Not.Contain("Record ="), "Prestart-ветка не должна передавать Record в ShowDebug-путь останова");
        });
    }

    private static string MethodBody(string methodName)
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\TrackHandler.cs");
        int signature = source.LastIndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.That(signature, Is.GreaterThanOrEqualTo(0), $"method not found: {methodName}");

        int start = source.LastIndexOf('\n', signature);
        return source.Substring(start, Math.Min(10000, source.Length - start));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"file not found: {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "repository root not found (AiRpa.sln)");
        return directory.FullName;
    }
}
