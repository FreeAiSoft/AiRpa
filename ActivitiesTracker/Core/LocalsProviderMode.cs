namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Источник данных панели «Локальные переменные» при остановке на точке останова.
    /// </summary>
    public enum LocalsProviderMode
    {
        /// <summary>Снимок значений из трекинг-записи (<see cref="TrackingLocalsProvider"/>).</summary>
        Tracking,

        /// <summary>Живые значения из <see cref="System.Activities.ActivityInstance"/> (<see cref="ActivityInstanceLocalsProvider"/>).</summary>
        ActivityInstance,

        /// <summary>Сначала <see cref="ActivityInstanceLocalsProvider"/>, при пустом/недоступном инстансе — фолбэк на <see cref="TrackingLocalsProvider"/>.</summary>
        Mixed
    }
}
