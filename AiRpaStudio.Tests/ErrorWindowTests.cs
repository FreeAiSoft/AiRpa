using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPA.Helpers;
using RPA.Views;

namespace AiRpaStudio.Tests;

/// <summary>
/// Окно ошибки студии: темизированное WPF-окно с readonly-текстом, который можно скопировать.
/// <para>
/// До задачи все ошибки показывались системным <c>MessageBox</c>: в тёмной теме это белое окно,
/// текст нельзя выделить и скопировать, а падение приложения выводило «Упс!! Исключение» из
/// WinForms-обработчика, который для WPF-приложения почти никогда не срабатывает
/// (UI-поток ловится <c>DispatcherUnhandledException</c>, а не <c>Application.ThreadException</c>).
/// </para>
/// <para>
/// Тесты делятся на стражи по исходникам (точки показа ошибок не должны вернуться к
/// <c>MessageBox</c>) и поведенческие проверки самого окна (readonly, выделение, копирование, тема).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ErrorWindowTests
{
    /// <summary>Файлы, где <c>MessageBox</c> для ошибок больше не допустим.</summary>
    private static readonly string[] ErrorOnlyDialogFiles =
    {
        @"AiRpaStudio\Views\ActivityCreatorView.xaml.cs",
        @"AiRpaStudio\Views\ProjectCreatorView.xaml.cs",
        @"AiRpaStudio\Views\VsProjectOpenView.xaml.cs",
        @"AiRpaStudio\Views\VsTemplateProjectCreatorView.xaml.cs"
    };

    private static readonly string[] ErrorLocalizationKeys =
    {
        "lang_error_title",
        "lang_error_crash_title",
        "lang_error_hint",
        "lang_error_copy",
        "lang_error_copied",
        "lang_close"
    };

    // --- Поведение окна: readonly-текст, выделение, копирование. ---

    [Test]
    public void ErrorView_MessageIsReadOnlyAndWrapped()
    {
        var view = new ErrorView { Message = "Не удалось сохранить файл" };

        TextBox box = MessageTextBoxOf(view);

        Assert.Multiple(() =>
        {
            Assert.That(box.IsReadOnly, Is.True, "текст ошибки нельзя редактировать — только читать и копировать");
            Assert.That(box.IsReadOnlyCaretVisible, Is.True,
                "без caret пользователь не увидит выделение и не сможет скопировать через Ctrl+C");
            Assert.That(box.TextWrapping, Is.EqualTo(TextWrapping.Wrap), "длинный стек-трейс должен переноситься");
            Assert.That(box.AcceptsReturn, Is.True, "многострочный текст ошибки не должен схлопываться в одну строку");
            Assert.That(box.Text, Is.EqualTo("Не удалось сохранить файл"));
            Assert.That(view.Message, Is.EqualTo("Не удалось сохранить файл"));
        });

        view.Close();
    }

    [Test]
    public void ErrorView_NullMessageBecomesEmptyText()
    {
        var view = new ErrorView { Message = null };

        try
        {
            Assert.That(MessageTextBoxOf(view).Text, Is.Empty, "null не должен попадать в TextBox");
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void ErrorView_CopyMessage_SelectsWholeText()
    {
        const string text = "Ошибка: файл занят другим процессом";
        var view = new ErrorView { Message = text };

        try
        {
            _ = view.CopyMessage();

            TextBox box = MessageTextBoxOf(view);
            Assert.Multiple(() =>
            {
                Assert.That(box.SelectionLength, Is.EqualTo(text.Length),
                    "если буфер обмена недоступен, выделение — единственный способ скопировать текст вручную");
                Assert.That(box.SelectedText, Is.EqualTo(text));
            });
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void ErrorView_CopyMessage_PutsTextToClipboard()
    {
        const string text = "Ошибка сборки: CS1002";
        var view = new ErrorView { Message = text };

        try
        {
            if (!view.CopyMessage())
            {
                Assert.Ignore("буфер обмена недоступен в этой сессии — проверено выделение текста");
            }

            Assert.That(Clipboard.GetText(), Is.EqualTo(text));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void ErrorView_CopyMessage_ShowsCopiedHint()
    {
        var view = new ErrorView { Message = "Ошибка" };

        try
        {
            Assert.That(view.IsCopiedHintVisible, Is.False, "подсказка скрыта до копирования");

            if (!view.CopyMessage())
            {
                Assert.Ignore("буфер обмена недоступен в этой сессии");
            }

            Assert.That(view.IsCopiedHintVisible, Is.True, "после копирования пользователь должен видеть подтверждение");
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void ErrorView_EmptyMessage_CopyReturnsFalse()
    {
        var view = new ErrorView { Message = string.Empty };

        try
        {
            Assert.That(view.CopyMessage(), Is.False, "пустой текст копировать нечего");
            Assert.That(view.IsCopiedHintVisible, Is.False);
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void ErrorView_BrushesComeFromPalette()
    {
        var view = new ErrorView();
        StudioBrushes.ApplyPalette(view.Resources, StudioPalette.Dark);

        try
        {
            TextBox box = MessageTextBoxOf(view);
            var background = box.Background as SolidColorBrush;

            Assert.Multiple(() =>
            {
                Assert.That(background, Is.Not.Null, "фон TextBox должен разрешиться из ключа Studio.SurfaceAlt");
                Assert.That(background.Color, Is.EqualTo(StudioPalette.Dark.SurfaceAlt),
                    "окно ошибки берёт цвета из палитры студии, а не из системных умолчаний");
            });
        }
        finally
        {
            view.Close();
        }
    }


    [Test]
    public void ErrorView_HeaderDefaultsToErrorTitle()
    {
        var view = new ErrorView();

        try
        {
            Assert.That(view.Header, Is.Not.Null.And.Not.Empty, "окно ошибки обязано иметь заголовок даже без ресурсов");
        }
        finally
        {
            view.Close();
        }
    }

    // --- Форматирование текста для пользователя. ---

    [Test]
    public void ErrorDialog_FormatCrashMessage_HasVersionTimeAndException()
    {
        var exception = new InvalidOperationException("дизайнер упал");

        string text = ErrorDialog.FormatCrashMessage(exception, "MainView.Load");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(DateTime.Now.ToString("yyyy-MM-dd")), "в тексте нет даты падения");
            Assert.That(text, Does.Contain("InvalidOperationException"), "без типа исключения текст бесполезен для диагностики");
            Assert.That(text, Does.Contain("дизайнер упал"), "в тексте нет сообщения исключения");
            Assert.That(text, Does.Contain("MainView.Load"), "контекст падения должен попадать в текст");
        });
    }

    [Test]
    public void ErrorDialog_FormatCrashMessage_SurvivesNullException()
    {
        Assert.That(ErrorDialog.FormatCrashMessage(null), Is.Not.Null.And.Not.Empty,
            "обработчик падения обязан показать хоть что-то даже без Exception");
    }

    [Test]
    public void ErrorDialog_BuildExceptionMessage_KeepsContextAndDetails()
    {
        var exception = new IOException("нет доступа к файлу");

        string text = ErrorDialog.BuildExceptionMessage(exception, "Failed to save workspace layout");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Failed to save workspace layout: нет доступа к файлу"));
            Assert.That(text, Does.Contain("IOException"), "в копируемом тексте должен остаться тип исключения");
        });
    }

    [Test]
    public void ErrorDialog_BuildExceptionMessage_WithoutExceptionReturnsContext()
    {
        Assert.That(ErrorDialog.BuildExceptionMessage(null, "контекст"), Is.EqualTo("контекст"));
    }

    // --- Стражи по исходникам: единая точка показа ошибок. ---

    [Test]
    public void ErrorOnlyDialogs_ShowErrorsThroughErrorDialog()
    {
        foreach (string file in ErrorOnlyDialogFiles)
        {
            string source = ReadSource(file);

            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Not.Contain("MessageBox.Show"),
                    $"{file}: ошибки показываются окном ErrorDialog, а не MessageBox");
                Assert.That(source, Does.Contain("ErrorDialog.Show("),
                    $"{file}: не подключено окно ошибки");
            });
        }
    }

    [Test]
    public void MainView_ShowsErrorsThroughErrorDialog()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("_ = MessageBox.Show(message);"),
                "OnSendMessage должен открывать окно ошибки");
            Assert.That(source, Does.Not.Contain("MessageBox.Show($\"Failed to save workspace layout"),
                "ошибка сохранения layout должна идти через ErrorDialog.ShowException");
            Assert.That(source, Does.Contain("ErrorDialog.Show(message, this)"),
                "MainView.OnSendMessage — точка показа ошибок модели");
            Assert.That(source, Does.Contain("ErrorDialog.ShowException("),
                "исключение сохранения layout должно показываться с контекстом");
        });
    }

    [Test]
    public void ProjectManagerControl_KeepsOnlyConfirmationMessageBox()
    {
        string source = ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml.cs");

        MatchCollection shows = Regex.Matches(source, "MessageBox\\.Show\\(",
            RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(shows.Count, Is.EqualTo(1),
                "после задачи в файле остаётся ровно один MessageBox — подтверждение удаления папки");
            Assert.That(source, Does.Contain("MessageBoxButton.YesNo"),
                "единственный MessageBox — это вопрос «удалить папку?», а не сообщение об ошибке");
            Assert.That(source, Does.Contain("ErrorDialog.Show("),
                "ошибки переименования/создания/удаления показываются окном ошибки");
            Assert.That(source, Does.Not.Contain("\"Ошибка\""),
                "заголовок ошибки больше не задаётся строковым литералом");
        });
    }

    [Test]
    public void Log_ReportsCrashThroughErrorDialog()
    {
        string source = ReadSource(@"AiRpaStudio\Logging\Log.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("System.Windows.Forms"),
                "студия — WPF-приложение: WinForms-обработчик исключений здесь не работает");
            Assert.That(source, Does.Not.Contain("MessageBox.Show"),
                "падение приложения показывает ErrorDialog, а не MessageBox");
            Assert.That(source, Does.Contain("ErrorDialog.ShowCrash"),
                "Log.ReportExceptionAndShowMessage должен открывать окно ошибки");
        });
    }

    [Test]
    public void App_HooksUnhandledExceptionBeforeStartup()
    {
        string app = ReadSource(@"AiRpaStudio\App.xaml.cs");
        string entryPoint = ReadSource(@"AiRpaStudio\EntryPoint.cs");

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("AppDomain.CurrentDomain.UnhandledException"),
                "падение на старте (App.xaml, главное окно) должно попадать в обработчик");
            Assert.That(app, Does.Contain("DispatcherUnhandledException"),
                "исключения UI-потока WPF ловит DispatcherUnhandledException");
            Assert.That(app, Does.Contain("Log.ReportExceptionAndShowMessage"),
                "обработчик старта обязан показывать окно ошибки через лог");
            Assert.That(entryPoint, Does.Not.Contain("System.Windows.Forms"),
                "подписка WinForms ThreadException для WPF бесполезна и удалена");
        });
    }

    [Test]
    public void ErrorView_Xaml_IsThemedAndCopyable()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\ErrorView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Background=\"{DynamicResource Studio.Surface}\""),
                "фон окна ошибки берётся из палитры, иначе в тёмной теме это белое окно");
            Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource Studio.Foreground}\""));
            Assert.That(xaml, Does.Contain("IsReadOnly=\"True\""), "текст ошибки не редактируется");
            Assert.That(xaml, Does.Contain("TextWrapping=\"Wrap\""), "стек-трейс переносится по словам");
            Assert.That(xaml, Does.Contain("VerticalScrollBarVisibility=\"Auto\""), "длинный текст прокручивается");
            Assert.That(xaml, Does.Contain("Margin=\"12\""), "внешний отступ диалогов студии — 12");
            Assert.That(xaml, Does.Contain("Studio.Semantic.Stop"),
                "иконка ошибки берёт семантический красный, а не акцент");
            Assert.That(xaml, Does.Not.Match("#[0-9A-Fa-f]{6,8}"), "зашитые цвета запрещены");
        });
    }

    [Test]
    public void ErrorView_UsesLocalizationKeys()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\ErrorView.xaml");
        string codeBehind = ReadSource(@"AiRpaStudio\Views\ErrorView.xaml.cs");

        Assert.Multiple(() =>
        {
            foreach (string key in new[] { "lang_error_title", "lang_error_hint", "lang_error_copy", "lang_close" })
            {
                Assert.That(xaml, Does.Contain("{DynamicResource " + key + "}"),
                    $"подпись {key} должна приходить из словаря ресурсов (локализация RU/EN)");
            }

            Assert.That(codeBehind, Does.Contain("LocalizeHelper.Get"),
                "код окна обязан подставлять подписи, когда словарь ресурсов ещё не загружен (краш на старте)");
        });
    }

    [Test]
    public void ErrorView_EnablesWindowCorner()
    {
        string codeBehind = ReadSource(@"AiRpaStudio\Views\ErrorView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Contain("OnSourceInitialized"), "HWND доступен только после OnSourceInitialized");
            Assert.That(codeBehind, Does.Contain("WindowCorner.Enable(this)"), "окно ошибки тоже скругляется");
        });
    }

    [Test]
    public void ErrorDialog_NeverThrowsAndFallsBackToDispatcher()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\ErrorDialog.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Dispatcher"), "окно из фонового потока показывается через Dispatcher");
            Assert.That(source, Does.Contain("ApartmentState.STA"),
                "если диспетчера уже нет (падение), окно открывается на собственном STA-потоке");
            Assert.That(source, Does.Contain("StudioBrushes.ApplyPalette"),
                "в оффлайн-пути палитра подкладывается локально: Application.Resources может быть уже мертва");
        });
    }

    [Test]
    public void Localization_HasErrorWindowKeys()
    {
        string neutral = ReadSource(@"AiRpaStudio\Resources\Main.resx");
        string russian = ReadSource(@"AiRpaStudio\Resources\Main.ru-RU.resx");

        foreach (string key in ErrorLocalizationKeys)
        {
            Assert.Multiple(() =>
            {
                Assert.That(neutral, Does.Contain("<data name=\"" + key + "\""), $"нет ключа {key} в Main.resx (EN)");
                Assert.That(russian, Does.Contain("<data name=\"" + key + "\""), $"нет ключа {key} в Main.ru-RU.resx (RU)");
            });
        }
    }

    [Test]
    public void ProjectFile_IncludesErrorWindow()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain(@"Views\ErrorView.xaml"), "страница XAML не добавлена в проект");
            Assert.That(csproj, Does.Contain(@"Views\ErrorView.xaml.cs"), "code-behind не добавлен в проект");
            Assert.That(csproj, Does.Contain(@"Helpers\ErrorDialog.cs"), "хелпер показа ошибок не добавлен в проект");
        });
    }

    private static TextBox MessageTextBoxOf(ErrorView view)
    {
        var box = view.FindName("MessageTextBox") as TextBox;
        Assert.That(box, Is.Not.Null, "в ErrorView нет TextBox с именем MessageTextBox");
        return box;
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
