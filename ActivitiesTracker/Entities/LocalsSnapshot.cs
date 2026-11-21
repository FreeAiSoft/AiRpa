using System.Collections.Generic;

namespace ActivitiesTracker.Entities
{
    public class LocalsSnapshot
    {
        public IReadOnlyList<LocalVar> Locals { get; }

        public string ActivityDisplayName { get; }

        /// <summary>Состояние активности (Executing/Closed/...), из которого получен снимок.</summary>
        public string ActivityState { get; }

        public LocalsSnapshot(IReadOnlyList<LocalVar> locals, string activityDisplayName, string activityState = null)
        {
            Locals = locals ?? new List<LocalVar>();
            ActivityDisplayName = activityDisplayName;
            ActivityState = activityState;
        }
    }
}