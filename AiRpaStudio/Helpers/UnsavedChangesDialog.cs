using System;
using System.Windows;
using RPA.Enums;
using RPA.Views;

namespace RPA.Helpers
{
    /// <summary>
    /// Точка показа вопроса «есть несохранённые изменения». Вместо системного
    /// <c>MessageBox</c> открывается темизированное <see cref="UnsavedChangesView"/>.
    /// <para>
    /// Возвращает <see cref="UnsavedChangesResult"/> — выбор пользователя:
    /// сохранить всё, продолжить без сохранения или отменить действие.
    /// </para>
    /// </summary>
    public static class UnsavedChangesDialog
    {
        /// <summary>Показывает диалог модально и возвращает выбор пользователя.</summary>
        /// <param name="owner">Окно-владелец; если не задано — главное окно приложения.</param>
        public static UnsavedChangesResult Show(Window owner = null)
        {
            UnsavedChangesView window = new UnsavedChangesView();

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
