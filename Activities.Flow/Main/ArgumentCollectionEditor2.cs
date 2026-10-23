using System.Activities.Presentation;
using System.Activities.Presentation.Converters;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.PropertyEditing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Activities.Flow.Main
{
    public class ArgumentCollectionEditor2 : DialogPropertyValueEditor
    {
        public ArgumentCollectionEditor2()
        {
            InlineEditorTemplate = new DataTemplate();

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            FrameworkElementFactory label = new FrameworkElementFactory(typeof(Label));
            Binding labelBinding = new Binding("Value");
            label.SetValue(ContentControl.ContentProperty, labelBinding);
            label.SetValue(FrameworkElement.MaxWidthProperty, 90.0);

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
                Title = "DisplayName XXX"
            };
            DynamicArgumentDesignerOptions options = options1;
            if (!activity.Properties[PropertyName].IsSet)
            {
                //Log.Output(PropertyName + " is not set");
                return;
            }
            ModelItem collection = (ModelItem)activity.Properties[PropertyName].Collection ?? activity.Properties[PropertyName].Dictionary;
            //
            if (collection == null)
            {
                //Log.Output(PropertyName + " is not a Collection or Dictionary");
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
