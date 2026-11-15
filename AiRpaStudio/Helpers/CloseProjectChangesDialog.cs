using System.Windows;
using RPA.Enums;
using RPA.Views;

namespace RPA.Helpers
{
    /// <summary>
    /// Точка показа вопроса «Закрыть проект с несохранёнными изменениями». Вместо системного
    /// <c>MessageBox</c> открывается темизированное <see cref="CloseProjectChangesView"/>.
    /// <para>
    /// Возвращает <see cref="CloseProjectChangesResult"/> — выбор пользователя:
    /// сохранить на диск, сохранить в буфер проекта или отменить закрытие.
    /// </para>
    /// </summary>
    public static class CloseProjectChangesDialog
    {
        /// <summary>Показывает диалог модально и возвращает выбор пользователя.</summary>
        /// <param name="owner">Окно-владелец; если не задано — главное окно приложения.</param>
        public static CloseProjectChangesResult Show(Window owner = null)
        {
            CloseProjectChangesView window = new CloseProjectChangesView();

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
            return window.Result;
        }

        /// <summary>
        /// Владелец окна: только уже показанное окно, иначе WPF бросает
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
    }
}
