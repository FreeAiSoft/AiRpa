using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LogControlLibrary.ViewModels
{
    /// <summary>
    /// Базовый класс для ViewModels с поддержкой INotifyPropertyChanged
    /// </summary>
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Событие изменения свойства
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Уведомление об изменении свойства
        /// </summary>
        /// <param name="propertyName">Имя свойства (автоматически определяется)</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Установка значения свойства с уведомлением об изменении
        /// </summary>
        /// <typeparam name="T">Тип свойства</typeparam>
        /// <param name="field">Поле для хранения значения</param>
        /// <param name="value">Новое значение</param>
        /// <param name="propertyName">Имя свойства (автоматически определяется)</param>
        /// <returns>True, если значение было изменено</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Установка значения свойства с дополнительным действием после изменения
        /// </summary>
        /// <typeparam name="T">Тип свойства</typeparam>
        /// <param name="field">Поле для хранения значения</param>
        /// <param name="value">Новое значение</param>
        /// <param name="onChanged">Действие, выполняемое после изменения значения</param>
        /// <param name="propertyName">Имя свойства (автоматически определяется)</param>
        /// <returns>True, если значение было изменено</returns>
        protected bool SetProperty<T>(ref T field, T value, Action onChanged, [CallerMemberName] string propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                onChanged?.Invoke();
                return true;
            }
            return false;
        }
    }
}