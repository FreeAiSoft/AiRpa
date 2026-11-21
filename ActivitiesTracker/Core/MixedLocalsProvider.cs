using System;
using System.Activities.Tracking;
using ActivitiesTracker.Entities;

namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Смешанный источник: сначала живые значения из
    /// <see cref="ActivityInstanceLocalsProvider"/>, при недоступности инстанса или пустом
    /// результате — фолбэк на <see cref="TrackingLocalsProvider"/>.
    /// </summary>
    public class MixedLocalsProvider : ILocalsProvider
    {
        private readonly ILocalsProvider instanceProvider = new ActivityInstanceLocalsProvider();
        private readonly ILocalsProvider trackingProvider = new TrackingLocalsProvider();

        public LocalsSnapshot GetSnapshot(ActivityStateRecord record)
        {
            if (record == null)
            {
                return new LocalsSnapshot(new LocalVar[0], null);
            }

            try
            {
                LocalsSnapshot instanceSnapshot = instanceProvider.GetSnapshot(record);
                if (instanceSnapshot != null && instanceSnapshot.Locals.Count > 0)
                {
                    return instanceSnapshot;
                }
            }
            catch (Exception)
            {
                // Reflection-доступ к internal ActivityInstance/DebugInfo может быть недоступен
                // на части окружений — тогда отдаём трекинг-снимок.
            }

            return trackingProvider.GetSnapshot(record);
        }
    }
}
