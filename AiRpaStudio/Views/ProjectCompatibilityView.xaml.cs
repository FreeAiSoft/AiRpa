using System;
using System.Windows;
using RPA.Helpers;
using RPA.VsProject;

namespace RPA.Views
{
    /// <summary>
    /// Окно отчёта о совместимости проекта активностей (пункт меню Tools).
    /// <para>
    /// Текст отчёта лежит в readonly <c>TextBox</c> и копируется через Ctrl+C или кнопкой
    /// «Копировать». Кнопка «Исправить автоматически» точечно перезаписывает версии пакетов
    /// (<c>packages.config</c>/<c>.csproj</c>/<c>.nuspec</c>), после чего отчёт пересчитывается.
    /// </para>
    /// </summary>
    public partial class ProjectCompatibilityView : Window
    {
        private ActivityCompatibilityReport _report;

        public ProjectCompatibilityView()
        {
            InitializeComponent();
            ApplyLocalizedFallbacks();
        }

        /// <summary>Показывает отчёт и подготавливает состояние окна.</summary>
        public void SetReport(ActivityCompatibilityReport report)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
            Refresh(report);
        }

        /// <summary>Копирует текст отчёта в буфер обмена.</summary>
        public bool CopyMessage()
        {
            string text = MessageTextBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            _ = MessageTextBox.Focus();
            MessageTextBox.SelectAll();

            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                return false;
            }

            if (string.IsNullOrEmpty(CopiedLabel.Text))
            {
                CopiedLabel.Text = Localize("lang_error_copied");
            }

            CopiedLabel.Visibility = Visibility.Visible;
            return true;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            _ = CopyMessage();
        }

        private void FixButton_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null || !_report.HasFixableIssues)
            {
                return;
            }

            ActivityCompatibilityFixResult result = new ActivityCompatibilityFixer().ApplyFixes(_report);

            // Пересчитываем отчёт после правок, чтобы пользователь видел актуальное состояние.
            ActivityCompatibilityReport refreshed;
            try
            {
                refreshed = new ActivityCompatibilityChecker().Check(_report.ProjectPath);
            }
            catch (Exception ex)
            {
                ErrorDialog.ShowException(ex, "Не удалось перечитать проект после исправления", this);
                return;
            }

            _report = refreshed;

            string summary = result.FixedCount > 0
                ? string.Format(Localize("lang_activity_compat_fix_done") ?? "Fixed: {0}", result.FixedCount)
                : Localize("lang_activity_compat_fix_nothing");

            if (result.FailedCount > 0)
            {
                summary += Environment.NewLine + string.Format(Localize("lang_activity_compat_fix_failed") ?? "Failed: {0}", result.FailedCount);
                foreach (string error in result.Errors)
                {
                    summary += Environment.NewLine + " - " + error;
                }
            }

            Refresh(refreshed, summary);
        }

        private void Refresh(ActivityCompatibilityReport report, string extraMessage = null)
        {
            string text = report.BuildText();
            if (!string.IsNullOrWhiteSpace(extraMessage))
            {
                text = extraMessage + Environment.NewLine + Environment.NewLine + text;
            }

            MessageTextBox.Text = text;
            MessageTextBox.CaretIndex = 0;
            MessageTextBox.ScrollToHome();
            CopiedLabel.Visibility = Visibility.Collapsed;

            bool compatible = report.IsCompatible;
            StatusLabel.Text = compatible
                ? (Localize("lang_activity_compat_compatible") ?? "Compatible")
                : (Localize("lang_activity_compat_problems") ?? "Problems found");

            OkIcon.Visibility = compatible ? Visibility.Visible : Visibility.Collapsed;
            ProblemIcon.Visibility = compatible ? Visibility.Collapsed : Visibility.Visible;

            FixButton.IsEnabled = report.HasFixableIssues;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplyLocalizedFallbacks()
        {
            if (string.IsNullOrEmpty(Title))
            {
                Title = Localize("lang_activity_compat_title");
            }

            if (string.IsNullOrEmpty(StatusLabel.Text))
            {
                StatusLabel.Text = Localize("lang_activity_compat_title");
            }

            if (string.IsNullOrEmpty(CopyButton.Content as string))
            {
                CopyButton.Content = Localize("lang_error_copy");
            }

            if (string.IsNullOrEmpty(FixButton.Content as string))
            {
                FixButton.Content = Localize("lang_activity_compat_fix");
            }

            if (string.IsNullOrEmpty(CloseButton.Content as string))
            {
                CloseButton.Content = Localize("lang_close");
            }
        }

        private static string Localize(string key, params object[] args)
        {
            try
            {
                return args == null || args.Length == 0
                    ? LocalizeHelper.Get(key)
                    : LocalizeHelper.Get(key, args.Length switch
                    {
                        1 => new object[] { args[0] },
                        2 => new object[] { args[0], args[1] },
                        3 => new object[] { args[0], args[1], args[2] },
                        _ => args
                    });
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
