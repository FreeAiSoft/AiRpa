using System;
using System.ComponentModel;
using System.Resources;

namespace ActivityLibraryBase.Attributes
{
    public class LocalizedCategoryAttribute : CategoryAttribute
    {
        private readonly string _resourceKey;
        private readonly Type _resourceType;
        private readonly ResourceManager _resourceManager;

        public LocalizedCategoryAttribute(string resourceKey, Type resourceType) : base(resourceKey)
        {
            _resourceKey = resourceKey;
            _resourceType = resourceType;
            _resourceManager = new ResourceManager(_resourceType);
        }

        protected override string GetLocalizedString(string value)
        {
            if (_resourceManager != null)
            {
                string localizedString = _resourceManager.GetString(_resourceKey);
                if (!string.IsNullOrEmpty(localizedString))
                {
                    return localizedString;
                }
            }
            // Fallback to the base behavior if localization fails or resource manager is not set
            return base.GetLocalizedString(value);
        }
    }
}
