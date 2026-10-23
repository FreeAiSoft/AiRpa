using System.Reflection;

namespace ActivitiesTracker.Extensions
{
    public static class ObjectExtensions
    {
        public static T GetProperty<T>(this object obj, string propertyName)
        {
            PropertyInfo invisibleMember = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic);
            T instance = (T)invisibleMember.GetValue(obj);
            return instance;
        }
    }
}
