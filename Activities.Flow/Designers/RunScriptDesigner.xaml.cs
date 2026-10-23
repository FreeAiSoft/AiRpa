using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.IO;
using System.Windows;

namespace Activities.Flow.Designers
{
    // Interaction logic for RunScriptDesigner.xaml
    public partial class RunScriptDesigner
    {
        public RunScriptDesigner()
        {
            InitializeComponent();
        }

        private void Button_Click_Change(object sender, RoutedEventArgs e)
        {
            ModelItemDictionary dictionary = ModelItem.Properties["Arguments"].Dictionary;
            DynamicArgumentDesignerOptions options = new DynamicArgumentDesignerOptions() { Title = "Run" };
            using (ModelEditingScope modelEditingScope = dictionary.BeginEdit())
            {
                if (DynamicArgumentDialog.ShowDialog(ModelItem, dictionary, Context, ModelItem.View, options))
                {
                    modelEditingScope.Complete();
                }
                else
                {
                    modelEditingScope.Revert();
                }
            }
        }

        private void Button_Click_Import(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the InArgument<string> from PathToXamlActivity property
                ModelProperty pathProperty = ModelItem.Properties["PathToXamlActivity"];
                if (pathProperty?.Value == null)
                {
                    _ = MessageBox.Show("Please specify the path to the activity first.", "Import Arguments", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get the Expression from InArgument
                ModelItem argumentModelItem = pathProperty.Value;
                ModelProperty expressionProperty = argumentModelItem.Properties["Expression"];

                if (expressionProperty?.Value == null)
                {
                    _ = MessageBox.Show("Please specify the path to the activity first.", "Import Arguments", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get the expression value (could be Literal or VisualBasicValue)
                ModelItem expressionModelItem = expressionProperty.Value;
                string activityPath = null;

                // Try to get value from Literal<string>
                ModelProperty valueProperty = expressionModelItem.Properties["Value"];
                if (valueProperty != null)
                {
                    activityPath = valueProperty.ComputedValue as string;
                }
                else
                {
                    // Try to get ExpressionText from VisualBasicValue<string>
                    ModelProperty expressionTextProperty = expressionModelItem.Properties["ExpressionText"];
                    if (expressionTextProperty != null)
                    {
                        string expressionText = expressionTextProperty.ComputedValue as string;
                        // Remove quotes if it's a string literal
                        if (!string.IsNullOrWhiteSpace(expressionText))
                        {
                            activityPath = expressionText.Trim().Trim('"');
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(activityPath))
                {
                    _ = MessageBox.Show("Please specify the path to the activity first.", "Import Arguments", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                activityPath = Path.Combine(ActivityLibraryBase.Helpers.DesignerHelper.ProjectDirPath, activityPath);

                // Check if file exists
                if (!File.Exists(activityPath))
                {
                    _ = MessageBox.Show($"Activity file not found: {activityPath}", "Import Arguments", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load the XAML activity
                Activity activity;
                using (StreamReader reader = new StreamReader(activityPath))
                {
                    activity = System.Activities.XamlIntegration.ActivityXamlServices.Load(reader);
                }

                // Get the dictionary of arguments
                ModelItemDictionary dictionary = ModelItem.Properties["Arguments"].Dictionary;

                // Clear existing arguments if user confirms
                if (dictionary.Count > 0)
                {
                    MessageBoxResult result = MessageBox.Show("This will replace existing arguments. Continue?", "Import Arguments", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.No)
                    {
                        return;
                    }
                }

                // Extract arguments from the activity's Properties collection
                int importedCount = 0;
                using (ModelEditingScope importScope = dictionary.BeginEdit())
                {
                    dictionary.Clear();

                    // Iterate through the activity's properties
                    foreach (DynamicActivityProperty property in ((DynamicActivity)activity).Properties)
                    {
                        // Create an argument based on the property type and direction
                        Argument argument = null;

                        if (property.Type == typeof(InArgument) ||
                            (property.Type.IsGenericType && property.Type.GetGenericTypeDefinition() == typeof(InArgument<>)))
                        {
                            // Create InArgument
                            Type argType = property.Type.IsGenericType ? property.Type.GetGenericArguments()[0] : typeof(object);
                            Type inArgType = typeof(InArgument<>).MakeGenericType(argType);
                            argument = (Argument)Activator.CreateInstance(inArgType);

                            // Copy default value if exists
                            if (property.Value != null && property.Value is Argument sourceArg)
                            {
                                argument.Expression = sourceArg.Expression;
                            }
                        }
                        else if (property.Type == typeof(OutArgument) ||
                                 (property.Type.IsGenericType && property.Type.GetGenericTypeDefinition() == typeof(OutArgument<>)))
                        {
                            // Create OutArgument
                            Type argType = property.Type.IsGenericType ? property.Type.GetGenericArguments()[0] : typeof(object);
                            Type outArgType = typeof(OutArgument<>).MakeGenericType(argType);
                            argument = (Argument)Activator.CreateInstance(outArgType);

                            // Copy default value if exists
                            if (property.Value != null && property.Value is Argument sourceArg)
                            {
                                argument.Expression = sourceArg.Expression;
                            }
                        }
                        else if (property.Type == typeof(InOutArgument) ||
                                 (property.Type.IsGenericType && property.Type.GetGenericTypeDefinition() == typeof(InOutArgument<>)))
                        {
                            // Create InOutArgument
                            Type argType = property.Type.IsGenericType ? property.Type.GetGenericArguments()[0] : typeof(object);
                            Type inOutArgType = typeof(InOutArgument<>).MakeGenericType(argType);
                            argument = (Argument)Activator.CreateInstance(inOutArgType);

                            // Copy default value if exists
                            if (property.Value != null && property.Value is Argument sourceArg)
                            {
                                argument.Expression = sourceArg.Expression;
                            }
                        }

                        if (argument != null)
                        {
                            // Add to dictionary
                            _ = dictionary.Add(property.Name, argument);
                            importedCount++;
                        }
                    }

                    importScope.Complete();
                }

                // Now show the dialog with imported arguments for editing
                DynamicArgumentDesignerOptions options = new DynamicArgumentDesignerOptions() { Title = $"Imported Arguments ({importedCount})" };
                using (ModelEditingScope editScope = dictionary.BeginEdit())
                {
                    if (DynamicArgumentDialog.ShowDialog(ModelItem, dictionary, Context, ModelItem.View, options))
                    {
                        editScope.Complete();
                    }
                    else
                    {
                        editScope.Revert();
                    }
                }
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Error importing arguments: {ex.Message}", "Import Arguments", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
