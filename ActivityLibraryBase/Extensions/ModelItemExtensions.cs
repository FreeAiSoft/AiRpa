using System.Activities.Presentation.Model;

namespace ActivityLibraryBase.Extensions
{
    public static class ModelItemExtensions
    {
        public static T GetValue<T>(this ModelItem modelItem, string name)
        {
            T result = default;
            if (modelItem.Properties[name] != null)
            {
                if (modelItem.Properties[name].Value == null)
                {
                    return result;
                }

                if (modelItem.Properties[name].Value.Properties["Expression"] != null)
                {
                    return modelItem.Properties[name].Value.Properties["Expression"].ComputedValue.TryCast<T>();
                }
            }
            return result;
        }
    }
}
