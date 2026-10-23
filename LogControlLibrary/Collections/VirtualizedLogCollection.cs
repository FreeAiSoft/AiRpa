using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LogControlLibrary.Models;

namespace LogControlLibrary.Collections
{
    /// <summary>
    /// Высокопроизводительная виртуализированная коллекция логов с поддержкой больших объемов данных
    /// </summary>
    public class VirtualizedLogCollection : IList<ILogEntry>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private readonly List<ILogEntry> _items;
        private readonly ReaderWriterLockSlim _lock;
        private int _maxCapacity;
        private bool _autoRemoveOldEntries;

        /// <summary>
        /// Событие изменения коллекции
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        /// <summary>
        /// Событие изменения свойства
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Максимальная емкость коллекции (по умолчанию 100,000 записей)
        /// </summary>
        public int MaxCapacity
        {
            get => _maxCapacity;
            set
            {
                if (_maxCapacity != value && value > 0)
                {
                    _maxCapacity = value;
                    OnPropertyChanged(nameof(MaxCapacity));
                    
                    // Обрезаем коллекцию если превышена новая емкость
                    TrimToCapacity();
                }
            }
        }

        /// <summary>
        /// Автоматическое удаление старых записей при превышении емкости
        /// </summary>
        public bool AutoRemoveOldEntries
        {
            get => _autoRemoveOldEntries;
            set
            {
                if (_autoRemoveOldEntries != value)
                {
                    _autoRemoveOldEntries = value;
                    OnPropertyChanged(nameof(AutoRemoveOldEntries));
                }
            }
        }

        /// <summary>
        /// Количество элементов в коллекции
        /// </summary>
        public int Count
        {
            get
            {
                try
                {
                    _lock.EnterReadLock();
                }
                catch (LockRecursionException ex)
                {
                    throw;
                }
                
                try
                {
                    return _items.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Доступ только для чтения
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="maxCapacity">Максимальная емкость коллекции</param>
        /// <param name="autoRemoveOldEntries">Автоматическое удаление старых записей</param>
        public VirtualizedLogCollection(int maxCapacity = 100000, bool autoRemoveOldEntries = true)
        {
            _items = new List<ILogEntry>();
            _lock = new ReaderWriterLockSlim();
            _maxCapacity = maxCapacity;
            _autoRemoveOldEntries = autoRemoveOldEntries;
        }

        /// <summary>
        /// Индексатор для доступа к элементам
        /// </summary>
        /// <param name="index">Индекс элемента</param>
        /// <returns>Элемент логирования</returns>
        public ILogEntry this[int index]
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _items[index];
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                NotifyCollectionChangedEventArgs collectionChangedArgs = null;
                
                _lock.EnterWriteLock();
                try
                {
                    _items[index] = value;
                    collectionChangedArgs = new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace, value, index);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
                
                // Вызываем событие ВНЕ блокировки
                if (collectionChangedArgs != null)
                {
                    OnCollectionChanged(collectionChangedArgs);
                }
            }
        }

        /// <summary>
        /// Добавление элемента в коллекцию
        /// </summary>
        /// <param name="item">Элемент для добавления</param>
        public void Add(ILogEntry item)
        {
            if (item == null) return;

            // Переменные для событий (чтобы вызвать их вне блокировки)
            NotifyCollectionChangedEventArgs collectionChangedArgs = null;
            bool wasReset = false;
            
            _lock.EnterWriteLock();
            try
            {
                // Проверяем емкость и удаляем старые записи если необходимо
                if (_autoRemoveOldEntries && _items.Count >= _maxCapacity)
                {
                    var toRemove = _maxCapacity / 10; // Удаляем 10% старых записей
                    if (toRemove > 0)
                    {
                        RemoveOldestEntries(toRemove);
                        wasReset = true; // Массовое удаление требует Reset
                    }
                }

                _items.Add(item);
                
                // Если были удаления, посылаем Reset, иначе Add
                if (wasReset)
                {
                    collectionChangedArgs = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                }
                else
                {
                    var newIndex = _items.Count - 1;
                    collectionChangedArgs = new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Add, item, newIndex);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
             }
            
            // Вызываем события ВНЕ блокировки
            if (collectionChangedArgs != null)
            {
                OnCollectionChanged(collectionChangedArgs);
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Массовое добавление элементов для улучшения производительности
        /// </summary>
        /// <param name="items">Коллекция элементов для добавления</param>
        public void AddRange(IEnumerable<ILogEntry> items)
        {
            if (items == null) return;

            var itemsList = items.ToList();
            if (itemsList.Count == 0) return;

            // Переменные для событий (чтобы вызвать их вне блокировки)
            NotifyCollectionChangedEventArgs collectionChangedArgs = null;
            bool wasReset = false;

            _lock.EnterWriteLock();
            try
            {
                // Проверяем емкость и удаляем старые записи если необходимо
                if (_autoRemoveOldEntries)
                {
                    var totalAfterAdd = _items.Count + itemsList.Count;
                    if (totalAfterAdd > _maxCapacity)
                    {
                        // Удаляем только необходимое количество старых записей
                        var toRemove = totalAfterAdd - _maxCapacity;
                        if (toRemove > 0)
                        {
                            RemoveOldestEntries(toRemove);
                            wasReset = true; // Массовое удаление требует Reset
                        }
                    }
                }

                _items.AddRange(itemsList);

                // При массовых операциях с удалением всегда используем Reset
                if (wasReset || itemsList.Count > 100)
                {
                    collectionChangedArgs = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                }
                else
                {
                    var startIndex = _items.Count - itemsList.Count;
                    collectionChangedArgs = new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Add, itemsList, startIndex);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            // Вызываем события ВНЕ блокировки
            if (collectionChangedArgs != null)
            {
                OnCollectionChanged(collectionChangedArgs);
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Очистка коллекции
        /// </summary>
        public void Clear()
        {
            bool wasCleared = false;
            
            _lock.EnterWriteLock();
            try
            {
                if (_items.Count > 0)
                {
                    _items.Clear();
                    wasCleared = true;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            
            // Вызываем события ВНЕ блокировки
            if (wasCleared)
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Проверка содержания элемента
        /// </summary>
        /// <param name="item">Элемент для поиска</param>
        /// <returns>True, если элемент найден</returns>
        public bool Contains(ILogEntry item)
        {
            _lock.EnterReadLock();
            try
            {
                return _items.Contains(item);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Копирование в массив
        /// </summary>
        /// <param name="array">Целевой массив</param>
        /// <param name="arrayIndex">Начальный индекс</param>
        public void CopyTo(ILogEntry[] array, int arrayIndex)
        {
            _lock.EnterReadLock();
            try
            {
                _items.CopyTo(array, arrayIndex);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Получение индекса элемента
        /// </summary>
        /// <param name="item">Элемент для поиска</param>
        /// <returns>Индекс элемента или -1 если не найден</returns>
        public int IndexOf(ILogEntry item)
        {
            _lock.EnterReadLock();
            try
            {
                return _items.IndexOf(item);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Вставка элемента по индексу
        /// </summary>
        /// <param name="index">Индекс для вставки</param>
        /// <param name="item">Элемент для вставки</param>
        public void Insert(int index, ILogEntry item)
        {
            NotifyCollectionChangedEventArgs collectionChangedArgs = null;
            
            _lock.EnterWriteLock();
            try
            {
                _items.Insert(index, item);
                collectionChangedArgs = new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, item, index);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            
            // Вызываем события ВНЕ блокировки
            if (collectionChangedArgs != null)
            {
                OnCollectionChanged(collectionChangedArgs);
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Удаление элемента
        /// </summary>
        /// <param name="item">Элемент для удаления</param>
        /// <returns>True, если элемент был удален</returns>
        public bool Remove(ILogEntry item)
        {
            NotifyCollectionChangedEventArgs collectionChangedArgs = null;
            bool removed = false;
            
            _lock.EnterWriteLock();
            try
            {
                var index = _items.IndexOf(item);
                if (index >= 0)
                {
                    _items.RemoveAt(index);
                    collectionChangedArgs = new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Remove, item, index);
                    removed = true;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            
            // Вызываем события ВНЕ блокировки
            if (collectionChangedArgs != null)
            {
                OnCollectionChanged(collectionChangedArgs);
                OnPropertyChanged(nameof(Count));
            }
            
            return removed;
        }

        /// <summary>
        /// Удаление элемента по индексу
        /// </summary>
        /// <param name="index">Индекс элемента для удаления</param>
        public void RemoveAt(int index)
        {
            NotifyCollectionChangedEventArgs collectionChangedArgs = null;
            
            _lock.EnterWriteLock();
            try
            {
                var item = _items[index];
                _items.RemoveAt(index);
                collectionChangedArgs = new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove, item, index);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            
            // Вызываем события ВНЕ блокировки
            if (collectionChangedArgs != null)
            {
                OnCollectionChanged(collectionChangedArgs);
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Удаление записей старше указанной даты
        /// </summary>
        /// <param name="cutoffDate">Дата, старше которой записи будут удалены</param>
        /// <returns>Количество удаленных записей</returns>
        public int RemoveEntriesOlderThan(DateTime cutoffDate)
        {
            int removedCount = 0;
            bool hasChanges = false;
            
            _lock.EnterWriteLock();
            try
            {
                var itemsToRemove = _items.Where(x => x.Timestamp < cutoffDate).ToList();
                foreach (var item in itemsToRemove)
                {
                    _items.Remove(item);
                }

                removedCount = itemsToRemove.Count;
                hasChanges = removedCount > 0;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            
            // Вызываем события ВНЕ блокировки
            if (hasChanges)
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                OnPropertyChanged(nameof(Count));
            }

            return removedCount;
        }

        /// <summary>
        /// Получение диапазона записей для виртуализации
        /// </summary>
        /// <param name="startIndex">Начальный индекс</param>
        /// <param name="count">Количество записей</param>
        /// <returns>Диапазон записей</returns>
        public IList<ILogEntry> GetRange(int startIndex, int count)
        {
            _lock.EnterReadLock();
            try
            {
                if (startIndex < 0 || startIndex >= _items.Count)
                    return new List<ILogEntry>();

                var actualCount = Math.Min(count, _items.Count - startIndex);
                var result = new List<ILogEntry>(actualCount);
                
                for (int i = startIndex; i < startIndex + actualCount; i++)
                {
                    result.Add(_items[i]);
                }

                return result;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Получение енумератора
        /// </summary>
        /// <returns>Енумератор коллекции</returns>
        public IEnumerator<ILogEntry> GetEnumerator()
        {
            _lock.EnterReadLock();
            try
            {
                // Создаем копию для безопасной итерации
                return _items.ToList().GetEnumerator();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Получение енумератора (не типизированный)
        /// </summary>
        /// <returns>Енумератор коллекции</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Удаление самых старых записей
        /// </summary>
        /// <param name="count">Количество записей для удаления</param>
        private void RemoveOldestEntries(int count)
        {
            if (count <= 0 || _items.Count == 0) return;

            var toRemove = Math.Min(count, _items.Count);
            
            // Удаляем старые записи с начала коллекции
            _items.RemoveRange(0, toRemove);
        }

        /// <summary>
        /// Обрезка коллекции до максимальной емкости
        /// </summary>
        private void TrimToCapacity()
        {
            bool hasChanges = false;
            
            _lock.EnterWriteLock();
            try
            {
                if (_items.Count > _maxCapacity)
                {
                    var toRemove = _items.Count - _maxCapacity;
                    RemoveOldestEntries(toRemove);
                    hasChanges = true;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            
            // Вызываем события ВНЕ блокировки
            if (hasChanges)
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Уведомление об изменении коллекции
        /// </summary>
        /// <param name="e">Аргументы события</param>
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Уведомление об изменении свойства
        /// </summary>
        /// <param name="propertyName">Имя свойства</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            Debug.WriteLine($"[VirtualizedLogCollection] OnPropertyChanged({propertyName}) вызван. Поток: {Thread.CurrentThread.ManagedThreadId}, Блокировка записи: {_lock.IsWriteLockHeld}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            Debug.WriteLine($"[VirtualizedLogCollection] OnPropertyChanged({propertyName}) завершен. Поток: {Thread.CurrentThread.ManagedThreadId}");
        }

        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}