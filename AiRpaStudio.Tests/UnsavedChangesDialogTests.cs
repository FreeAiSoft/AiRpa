using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using RPA.Enums;
using RPA.Views;

namespace AiRpaStudio.Tests;

/// <summary>
/// Темизированный диалог «есть несохранённые изменения»: показывается вместо системного
/// <c>MessageBox</c> при закрытии студии и при открытии/создании другого проекта.
/// <para>
/// Тесты делятся на поведенческие (кнопки возвращают правильный <see cref="UnsavedChangesResult"/>)
/// и стражи по исходникам (тема, локализация, точки интеграции в <c>Main</c>/<c>MainView</c>).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class UnsavedChangesDialogTests
{
    private static readonly string[] UnsavedLocalizationKeys =
    {
        "lang_unsaved_title",
        "lang_unsaved_message",
        "lang_unsaved_save",
        "lang_unsaved_dont_save",
        "lang_unsaved_cancel"
    };

    // --- Поведение окна. ---

    [Test]
    public void UnsavedChangesView_DefaultResultIsCancel()
    {
        var view = new UnsavedChangesView();

        try
        {
            Assert.That(view.Result, Is.EqualTo(UnsavedChangesResult.Cancel),
                "закрытие крестиком/Alt+F4/Esc без явного выбора обязано означать «Отмена»");
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void UnsavedChangesView_SaveButtonSetsResult()
    {
        var view = new UnsavedChangesView();

        try
        {
            ButtonOf(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.Result, Is.EqualTo(UnsavedChangesResult.Save));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void UnsavedChangesView_DiscardButtonSetsResult()
    {
        var view = new UnsavedChangesView();

        try
        {
            ButtonOf(view, "DiscardButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.Result, Is.EqualTo(UnsavedChangesResult.Discard));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void UnsavedChangesView_CancelButtonSetsResult()
    {
        var view = new UnsavedChangesView();

        try
        {
            ButtonOf(view, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.Result, Is.EqualTo(UnsavedChangesResult.Cancel));
        }
        finally
        {
            view.Close();
        }
    }

    // --- Тема и скругление. ---

    [Test]
    public void UnsavedChangesView_Xaml_IsThemed()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\UnsavedChangesView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Background=\"{DynamicResource Studio.Surface}\""),
                "фон диалога берётся из палитры студии, иначе в тёмной теме это белое окно");
            Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource Studio.Foreground}\""));
            Assert.That(xaml, Does.Contain("<controls:StudioTitleBar"), "заголовок рисует студия, а не системный caption");
            Assert.That(xaml, Does.Contain("ShowMinimize=\"False\""), "окно не в панели задач — сворачивание скрыто");
            Assert.That(xaml, Does.Contain("CornerRadius=\"8\""), "окно скругляется как остальные диалоги студии");
            Assert.That(xaml, Does.Contain("Margin=\"12\""), "внешний отступ контента диалогов студии — 12");
            Assert.That(xaml, Does.Contain("Studio.Semantic.Warning"),
                "иконка предупреждения берёт семантический цвет, а не акцент");
            Assert.That(xaml, Does.Not.Match("#[0-9A-Fa-f]{6,8}"), "зашитые цвета запрещены");
        });
    }

    [Test]
    public void UnsavedChangesView_EnablesWindowCorner()
    {
        string codeBehind = ReadSource(@"AiRpaStudio\Views\UnsavedChangesView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Contain("OnSourceInitialized"), "HWND доступен только после OnSourceInitialized");
            Assert.That(codeBehind, Does.Contain("WindowCorner.Enable(this)"), "диалог тоже скругляется");
        });
    }


    [Test]
    public void UnsavedChangesView_Xaml_IsSizedDownAndIconInline()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\UnsavedChangesView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Height=\"200\""), "высота уменьшена относительно 240 в 1,2 раза");
            Assert.That(xaml, Does.Contain("Width=\"467\""), "ширина уменьшена относительно 560 в 1,2 раза");
            Assert.That(xaml, Does.Contain("MinHeight=\"183\""), "минимальная высота уменьшена в 1,2 раза");
            Assert.That(xaml, Does.Contain("MinWidth=\"400\""), "минимальная ширина уменьшена в 1,2 раза");

            Assert.That(xaml, Does.Contain("Margin=\"0,0,10,0\""),
                "отступ 10px от иконки предупреждения до текста сообщения");
            Assert.That(xaml, Does.Contain("<materialIcons:MaterialIcon Kind=\"AlertCircleOutline\""),
                "иконка предупреждения стоит в строке сообщения");
        });
    }

    [Test]
    public void UnsavedChangesView_UsesLocalizationKeys()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\UnsavedChangesView.xaml");
        string codeBehind = ReadSource(@"AiRpaStudio\Views\UnsavedChangesView.xaml.cs");

        Assert.Multiple(() =>
        {
            foreach (string key in UnsavedLocalizationKeys)
            {
                Assert.That(xaml, Does.Contain("{DynamicResource " + key + "}"),
                    $"подпись {key} должна приходить из словаря ресурсов (локализация RU/EN)");
            }

            Assert.That(codeBehind, Does.Contain("LocalizeHelper.Get"),
                "код окна обязан подставлять подписи, когда словарь ресурсов ещё не загружен");
        });
    }

    [Test]
    public void Localization_HasUnsavedChangesKeys()
    {
        string neutral = ReadSource(@"AiRpaStudio\Resources\Main.resx");
        string russian = ReadSource(@"AiRpaStudio\Resources\Main.ru-RU.resx");

        foreach (string key in UnsavedLocalizationKeys)
        {
            Assert.Multiple(() =>
            {
                Assert.That(neutral, Does.Contain("<data name=\"" + key + "\""), $"нет ключа {key} в Main.resx (EN)");
                Assert.That(russian, Does.Contain("<data name=\"" + key + "\""), $"нет ключа {key} в Main.ru-RU.resx (RU)");
            });
        }
    }

    // --- Логика «есть ли изменения / сохранить всё» в модели. ---

    [Test]
    public void Main_DetectsUnsavedChanges()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public bool HasUnsavedChanges"),
                "модель должна уметь отвечать, есть ли несохранённые изменения");
            Assert.That(source, Does.Contain("WorkflowDesigners.Any(d => d.IsEdited)"),
                "workflow-вкладки считаются изменёнными по IsEdited");
            Assert.That(source, Does.Contain("OpenCodeDocuments.Any(d => d.IsDirty)"),
                "редакторы кода/XAML считаются изменёнными по IsDirty");
        });
    }

    [Test]
    public void Main_SaveAllChanges_SavesWorkflowsAndCodeDocuments()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public void SaveAllChanges()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("WorkflowSaveAll()"), "все изменённые workflow сохраняются разом");
            Assert.That(method, Does.Contain("OpenCodeDocuments.Where(d => d.IsDirty)"),
                "сохраняются только изменённые редакторы кода/XAML");
            Assert.That(method, Does.Contain("document.Save()"), "файл редактора кода пишется на диск");
        });
    }

    [Test]
    public void Main_ConfirmSaveOrDiscard_UsesDialog()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public bool ConfirmSaveOrDiscardChanges()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("UnsavedChangesDialog.Show("), "вопрос показывает темизированный диалог");
            Assert.That(method, Does.Contain("UnsavedChangesResult.Save"), "кнопка «Сохранить» сохраняет изменения");
            Assert.That(method, Does.Contain("UnsavedChangesResult.Discard"), "кнопка «Не сохранять» продолжает без сохранения");
        });
    }

    // --- Интеграция: открытие/создание проекта. ---

    [Test]
    public void Main_OpenProjectCommands_ConfirmBeforeChangingProject()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        foreach (string command in new[]
                 {
                     "CmdTryLoadProject",
                     "CmdVsOpenProject",
                     "CmdWorkflowNewProcess",
                     "CmdVsNewProjectFromTemplate"
                 })
        {
            string method = MethodBody(source, "public void " + command + "(");

            Assert.Multiple(() =>
            {
                Assert.That(method, Does.Contain("ConfirmSaveOrDiscardChanges()"),
                    $"{command}: перед открытием/созданием проекта обязана быть проверка несохранённых изменений");
                Assert.That(method, Does.Contain("return;"),
                    $"{command}: при отмене команда обязана завершаться, не меняя проект");
            });
        }
    }

    // --- Интеграция: закрытие студии. ---

    [Test]
    public void MainView_OnClosing_ConfirmsBeforeSavingSession()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs"), "OnClosing");

        int confirm = method.IndexOf("ConfirmSaveOrDiscardChanges()", StringComparison.Ordinal);
        int cancel = method.IndexOf("e.Cancel = true;", StringComparison.Ordinal);
        int saveSession = method.IndexOf("SaveSessionState", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(confirm, Is.GreaterThanOrEqualTo(0), "закрытие студии обязано спрашивать про несохранённые изменения");
            Assert.That(cancel, Is.GreaterThanOrEqualTo(0), "при отмене окно не закрывается (e.Cancel = true)");
            Assert.That(saveSession, Is.GreaterThan(confirm),
                "проверка идёт до финального сохранения состояния вкладок");
            Assert.That(cancel, Is.LessThan(saveSession),
                "отмена должна случиться до сохранения сессии");
        });
    }

    // --- Стражи: никакого системного MessageBox. ---

    [Test]
    public void UnsavedChangesDialog_DoesNotUseMessageBox()
    {
        string helper = ReadSource(@"AiRpaStudio\Helpers\UnsavedChangesDialog.cs");
        string view = ReadSource(@"AiRpaStudio\Views\UnsavedChangesView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Not.Contain("MessageBox.Show"),
                "вопрос показывается темизированным окном, а не MessageBox");
            Assert.That(view, Does.Not.Contain("MessageBox.Show"),
                "кнопки диалога не должны падать в системный MessageBox");
        });
    }

    [Test]
    public void ProjectFile_IncludesUnsavedChangesDialog()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain(@"Views\UnsavedChangesView.xaml"), "страница XAML не добавлена в проект");
            Assert.That(csproj, Does.Contain(@"Views\UnsavedChangesView.xaml.cs"), "code-behind не добавлен в проект");
            Assert.That(csproj, Does.Contain(@"Helpers\UnsavedChangesDialog.cs"), "хелпер диалога не добавлен в проект");
            Assert.That(csproj, Does.Contain(@"Enums\UnsavedChangesResult.cs"), "перечисление результата не добавлено в проект");
        });
    }

    // --- Хелперы. ---

    private static Button ButtonOf(UnsavedChangesView view, string name)
    {
        var button = view.FindName(name) as Button;
        Assert.That(button, Is.Not.Null, $"в UnsavedChangesView нет кнопки с именем {name}");
        return button;
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodSignature)
    {
        Match match = Regex.Match(
            source,
            Regex.Escape(methodSignature));

        Assert.That(match.Success, Is.True, $"не найден метод {methodSignature}");
        return source.Substring(match.Index, Math.Min(1600, source.Length - match.Index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
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
