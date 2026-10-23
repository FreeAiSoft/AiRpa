using System.Activities.Expressions;

namespace ActivityLibraryBase.Extensions
{
    public static class ObjectExtensions
    {
        public static T TryCast<T>(this object instance)
        {
            return TryCast(instance, out T result) ? result : default;
        }

        public static bool TryCast<T>(this object instance, out T result)
        {
            if (instance is T t)
            {
                result = t;
                return true;
            }

            if (instance is Literal<T> literal)
            {
                result = literal.Value;
                return true;
            }

            result = default;
            return false;
        }
    }
}
