using System.Activities.Presentation.Converters;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.PropertyEditing;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.Activities.Presentation;
using ActivityLibraryBase.Extensions;
using System;

namespace ActivityLibraryBase.BaseActivities
{
    public class ArgumentCollectionEditor : DialogPropertyValueEditor
    {
        public ArgumentCollectionEditor()
        {
            InlineEditorTemplate = new DataTemplate();

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            FrameworkElementFactory label = new FrameworkElementFactory(typeof(Label));
            Binding labelBinding = new Binding("Value");
#pragma warning disable IDE0002 // Simplify Member Access
            label.SetValue(Label.ContentProperty, labelBinding);
            label.SetValue(Label.MaxWidthProperty, 90.0);
#pragma warning restore IDE0002 // Simplify Member Access
            stack.AppendChild(label);
            FrameworkElementFactory editModeSwitch = new FrameworkElementFactory(typeof(EditModeSwitchButton));
            editModeSwitch.SetValue(EditModeSwitchButton.TargetEditModeProperty, PropertyContainerEditMode.Dialog);
            stack.AppendChild(editModeSwitch);
            InlineEditorTemplate.VisualTree = stack;
        }

        public override void ShowDialog(PropertyValue propertyValue, IInputElement commandSource)
        {
            // https://stackoverflow.com/questions/8731605/exposing-collection-of-arguments-for-activity-in-property-grid
            string PropertyName = propertyValue.ParentProperty.PropertyName;
            ModelItem activity = new ModelPropertyEntryToOwnerActivityConverter().Convert(propertyValue.ParentProperty, typeof(ModelItem), false, null) as ModelItem;

            DynamicArgumentDesignerOptions options1 = new DynamicArgumentDesignerOptions
            {
                Title = activity.GetValue<string>("DisplayName") ?? "unknow DisplayName"
            };
            DynamicArgumentDesignerOptions options = options1;
            if (!activity.Properties[PropertyName].IsSet)
            {
                Console.WriteLine(PropertyName + " is not set");
                return;
            }
            ModelItem collection = activity.Properties[PropertyName].Collection;
            if (collection == null)
            {
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                collection = activity.Properties[PropertyName].Dictionary;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
                Console.WriteLine(PropertyName + " is not a Collection or Dictionary");
                return;
            }
            using (ModelEditingScope scope = collection.BeginEdit(PropertyName + "Editing"))
            {
                if (DynamicArgumentDialog.ShowDialog(activity, collection, activity.GetEditingContext(), activity.View, options))
                {
                    scope.Complete();
                }
                else
                {
                    scope.Revert();
                }
            }
        }
    }
}
