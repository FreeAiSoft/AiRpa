using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using RPA.Views;

namespace RPA.Helpers
{
    /// <summary>
    /// Единственная точка показа ошибок в студии: вместо <c>MessageBox</c> открывается
    /// темизированное <see cref="ErrorView"/> с readonly-текстом, который можно
    /// скопировать.
    /// <para>
    /// Хелпер обязан работать в самых плохих условиях — из обработчика падения приложения,
    /// то есть когда окно ещё не создано или диспетчер уже остановлен. Поэтому три пути:
    /// </para>
    /// <list type="number">
    /// <item>UI-поток — модальное окно поверх владельца (как раньше <c>MessageBox</c>);</item>
    /// <item>фоновый поток — окно показывается через <see cref="Dispatcher"/> с таймаутом;</item>
    /// <item>диспетчера нет (падение на старте/после <c>Shutdown</c>) — окно на собственном
    /// STA-потоке со своей копией палитры (<see cref="StudioBrushes.ApplyPalette"/>), иначе
    /// <c>{DynamicResource Studio.*}</c> пустые.</item>
    /// </list>
    /// <para>
    /// Окно не создаётся через <c>MvvmFactory</c> намеренно: во время падения нет ни модели,
    /// ни DataContext, а ошибку показать нужно. Это осознанное отступление от MVVM-правила.
    /// </para>
    /// </summary>
    public static class ErrorDialog
    {
        private const string ErrorTitleKey = "lang_error_title";
        private const string CrashTitleKey = "lang_error_crash_title";

        /// <summary>Сколько ждём UI-поток, прежде чем считать, что он занят/умер.</summary>
        private static readonly TimeSpan UiThreadTimeout = TimeSpan.FromMinutes(2);

        /// <summary>Сколько ждём, пока пользователь прочитает текст падения в оффлайн-окне.</summary>
        private static readonly TimeSpan OfflineDialogTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Показывает сообщение об ошибке.</summary>
        /// <param name="message">Текст ошибки (показывается as-is).</param>
        /// <param name="owner">Окно-владелец; если не задано — главное окно приложения.</param>
        public static void Show(string message, Window owner = null)
        {
            ShowCore(message ?? string.Empty, owner, null);
        }

        /// <summary>Показывает исключение: контекст + сообщение + полный <c>ToString()</c>.</summary>
        public static void ShowException(Exception ex, string context = null, Window owner = null)
        {
            ShowCore(BuildExceptionMessage(ex, context), owner, null);
        }

        /// <summary>
        /// Показывает падение приложения (последний шанс): текст с версией, временем и стеком.
        /// </summary>
        public static void ShowCrash(Exception ex, string context = null)
        {
            ShowCore(FormatCrashMessage(ex, context), null, CrashTitleKey);
        }

        /// <summary>
        /// Текст для падения: версия сборки, время, необязательный контекст и полное исключение —
        /// ровно то, что раньше выводил <c>Log.ReportExceptionAndShowMessage</c>.
        /// </summary>
        public static string FormatCrashMessage(Exception ex, string context = null)
        {
            StringBuilder builder = new StringBuilder();
            _ = builder.Append(GetVersion()).Append(' ').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _ = builder.AppendLine();
            _ = builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(context))
            {
                _ = builder.AppendLine(context.Trim());
                _ = builder.AppendLine();
            }

            _ = builder.Append(ex != null ? ex.ToString() : "<null>");
            return builder.ToString();
        }

        /// <summary>
        /// Текст для ожидаемой ошибки: «контекст: сообщение» + полное исключение под ним.
        /// </summary>
        public static string BuildExceptionMessage(Exception ex, string context = null)
        {
            string trimmedContext = context?.Trim().TrimEnd(':');

            if (ex == null)
            {
                return trimmedContext ?? string.Empty;
            }

            string head = string.IsNullOrEmpty(trimmedContext)
                ? ex.Message
                : $"{trimmedContext}: {ex.Message}";

            return $"{head}\r\n\r\n{ex}";
        }

        private static void ShowCore(string message, Window owner, string titleKey)
        {
            string text = message ?? string.Empty;

            Application application = Application.Current;
            Dispatcher dispatcher = application?.Dispatcher;

            if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                if (dispatcher.CheckAccess())
                {
                    if (ShowModal(text, owner, titleKey))
                    {
                        return;
                    }
                }
                else
                {
                    // Фоновый поток: окно живёт в UI-потоке. Таймаут — чтобы не подвесить поток навсегда.
                    bool shown = false;
                    try
                    {
                        shown = dispatcher.Invoke(
                            new Func<bool>(() => ShowModal(text, null, titleKey)),
                            DispatcherPriority.Send,
                            CancellationToken.None,
                            UiThreadTimeout);
                    }
                    catch (Exception)
                    {
                        shown = false;
                    }

                    if (shown)
                    {
                        return;
                    }
                }
            }

            ShowWithoutDispatcher(text, titleKey);
        }

        /// <summary>Показывает окно модально. Возвращает <c>false</c>, если окно создать не удалось.</summary>
        private static bool ShowModal(string text, Window owner, string titleKey)
        {
            try
            {
                ErrorView window = new ErrorView { Message = text };
                ApplyTitle(window, titleKey);
                EnsurePalette(window);

                Window ownerWindow = ResolveOwner(owner);
                if (ownerWindow != null)
                {
                    window.Owner = ownerWindow;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                _ = window.ShowDialog();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Оффлайн-путь: собственный STA-поток, своя палитра, без <see cref="Application"/>.</summary>
        private static void ShowWithoutDispatcher(string text, string titleKey)
        {
            Exception failure = null;

            Thread thread = new Thread(() =>
            {
                try
                {
                    if (!ShowModal(text, null, titleKey))
                    {
                        failure = new InvalidOperationException("окно ошибки не создалось");
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            })
            {
                IsBackground = false,
                Name = "ErrorDialog"
            };

            try
            {
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            catch (Exception)
            {
                ShowSystemMessageBox(text);
                return;
            }

            // Процесс может уже завершаться: держим поток, пока пользователь не закроет окно.
            _ = thread.Join(OfflineDialogTimeout);

            if (failure != null)
            {
                ShowSystemMessageBox(text);
            }
        }

        private static void ApplyTitle(ErrorView window, string titleKey)
        {
            if (string.IsNullOrEmpty(titleKey))
            {
                return;
            }

            string title = Localize(titleKey);
            if (string.IsNullOrEmpty(title))
            {
                return;
            }

            window.Header = title;
            window.Title = title;
        }

        /// <summary>
        /// Кисти <c>Studio.*</c> живут в <c>Application.Resources</c>; в оффлайн-пути их нет,
        /// поэтому палитра подкладывается локально в ресурсы окна.
        /// </summary>
        private static void EnsurePalette(ErrorView window)
        {
            ResourceDictionary applicationResources = Application.Current?.Resources;

            if (applicationResources != null && applicationResources.Contains(StudioBrushes.SurfaceKey))
            {
                return;
            }

            StudioBrushes.ApplyPalette(window.Resources, StudioPalette.For(StudioPalette.DetectTheme(applicationResources)));
        }

        /// <summary>
        /// Владелец окна ошибки: только уже показанное окно, иначе WPF бросает
        /// <c>InvalidOperationException</c> («Cannot set Owner to a window that has not been shown»).
        /// </summary>
        private static Window ResolveOwner(Window owner)
        {
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                return owner;
            }

            Window main = Application.Current?.MainWindow;

            return main != null && main.IsLoaded && main.IsVisible ? main : null;
        }

        private static void ShowSystemMessageBox(string text)
        {
            try
            {
                _ = MessageBox.Show(
                    text,
                    Localize(ErrorTitleKey) ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // Показать ошибку больше нечем — молчим, чтобы не уронить обработчик падения.
            }
        }

        private static string Localize(string key)
        {
            try
            {
                return LocalizeHelper.Get(key);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetVersion()
        {
            try
            {
                Version version = Assembly.GetEntryAssembly()?.GetName().Version;
                return version != null ? version.ToString() : "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
