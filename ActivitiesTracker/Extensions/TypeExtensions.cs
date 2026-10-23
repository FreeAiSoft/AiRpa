using System;
using System.Linq;

namespace ActivitiesTracker.Extensions
{
    public static class TypeExtensions
    {
        public static string GetFormattedName(this Type type)
        {
            if (type.IsGenericType)
            {
                string text = (from x in type.GetGenericArguments()
                               select x.Name).Aggregate((x1, x2) => x1 + ", " + x2);
                return type.Name.Substring(0, type.Name.IndexOf("`")) + "<" + text + ">";
            }

            return type.Name;
        }
    }
}
