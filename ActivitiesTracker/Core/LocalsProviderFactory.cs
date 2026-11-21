namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Создаёт провайдер данных панели «Локальные переменные» по выбранному режиму.
    /// </summary>
    public static class LocalsProviderFactory
    {
        public static ILocalsProvider Create(LocalsProviderMode mode)
        {
            switch (mode)
            {
                case LocalsProviderMode.ActivityInstance:
                    return new ActivityInstanceLocalsProvider();

                case LocalsProviderMode.Mixed:
                    return new MixedLocalsProvider();

                case LocalsProviderMode.Tracking:
                default:
                    return new TrackingLocalsProvider();
            }
        }
    }
}
