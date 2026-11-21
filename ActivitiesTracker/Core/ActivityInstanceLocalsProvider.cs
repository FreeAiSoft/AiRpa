using System;
using System.Activities;
using System.Activities.Tracking;
using System.Reflection;
using ActivitiesTracker.Entities;

namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Вариант B: живые значения из <see cref="System.Activities.ActivityInstance"/>.
    /// <para>
    /// В отличие от <see cref="TrackingLocalsProvider"/> (вариант A), читает значения напрямую из
    /// живой <see cref="Location"/> и поэтому, помимо переменных/аргументов текущего scope, отдаёт
    /// псевдопеременную <c>this</c> (<see cref="ActivityInstance"/>). Scope вычисляет
    /// <see cref="LocalsScopeResolver"/> с учётом <c>RelationshipToParent</c> и границы
    /// implementation (см. <c>Locals_Scope_Correctness_Plan.md</c>).
    /// </para>
    /// <para>
    /// Сам <see cref="ActivityInstance"/> извлекается из внутреннего свойства
    /// <c>ActivityInfo.Instance</c> трекинг-записи — живой инстанс, который WF кладёт в запись
    /// <see cref="ActivityStateRecord"/> при её создании из <c>ActivityInstance</c>. Никакого
    /// отдельного канала «runtime → UI» и встраивания DebugController/StateManager не требуется.
    /// </para>
    /// </summary>
    public class ActivityInstanceLocalsProvider : ILocalsProvider
    {
        private static readonly PropertyInfo ActivityInfoInstanceProperty = typeof(ActivityInfo)
            .GetProperty("Instance", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly LocalsScopeResolver resolver = new LocalsScopeResolver();

        public LocalsSnapshot GetSnapshot(ActivityStateRecord record)
        {
            if (record == null)
            {
                return new LocalsSnapshot(new LocalVar[0], null);
            }

            string displayName = record.Activity?.Name;

            ActivityInstance instance = GetActivityInstance(record.Activity);
            if (instance == null)
            {
                return new LocalsSnapshot(new LocalVar[0], displayName);
            }

            return resolver.Resolve(instance, record.State);
        }

        private static ActivityInstance GetActivityInstance(ActivityInfo activityInfo)
        {
            if (activityInfo == null || ActivityInfoInstanceProperty == null)
            {
                return null;
            }

            return ActivityInfoInstanceProperty.GetValue(activityInfo) as ActivityInstance;
        }
    }
}