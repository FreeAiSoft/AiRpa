using System;
using ActivitiesTracker.Enums;

namespace ActivitiesTracker.Extensions
{
    public static class StringExtensions
    {

        public static ActivityState ToActityState(this string activityInstanceState)
        {
            switch (activityInstanceState)
            {
                case "Executing":
                    return ActivityState.Executing;
                case "Closed":
                    return ActivityState.Closed;
                case "Canceled":
                    return ActivityState.Canceled;
                case "Faulted":
                    return ActivityState.Faulted;
                default:
                    throw new ArgumentException($"Unknow state {activityInstanceState}");
            }
        }
    }
}
