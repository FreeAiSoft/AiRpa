using System.Activities.Tracking;
using ActivitiesTracker.Entities;

namespace ActivitiesTracker.Core
{
    public class TrackingLocalsProvider : ILocalsProvider
    {
        public LocalsSnapshot GetSnapshot(ActivityStateRecord record)
        {
            if (record == null)
            {
                return new LocalsSnapshot(new LocalVar[0], null);
            }

            return new LocalsSnapshot(LocalsScopeTracker.GetVisibleLocals(record), record.Activity?.Name);
        }
    }
}