using System.Activities.Tracking;
using ActivitiesTracker.Entities;

namespace ActivitiesTracker.Core
{
    public interface ILocalsProvider
    {
        LocalsSnapshot GetSnapshot(ActivityStateRecord record);
    }
}