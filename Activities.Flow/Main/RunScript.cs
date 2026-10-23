using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Markup;
using Activities.Flow.Designers;
using Activities.Flow.Main;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;

namespace Activities.Flow
{
    [LocalizedDescription("start_activity_desc", typeof(Resources.ActivitiesFlow))]
    [LocalizedDisplayName("start_activity", typeof(Resources.ActivitiesFlow))]
    [Designer(typeof(RunScriptDesigner))]
    [ContentProperty("Parameters")]
    public sealed class RunScript : NativeActivity
    {
        [Browsable(true)]
        [LocalizedDescription("args_desc", typeof(Resources.ActivitiesFlow))]
        [LocalizedDisplayName("args", typeof(Resources.ActivitiesFlow))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public Dictionary<string, Argument> Arguments { get; set; }

        [LocalizedDescription("path_to_activity_desc", typeof(Resources.ActivitiesFlow))]
        [LocalizedDisplayName("path_to_activity", typeof(Resources.ActivitiesFlow))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        [RequiredArgument]
        public InArgument<string> PathToXamlActivity { get; set; }

        [LocalizedDescription("activities_base_is_enable_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_is_enable", typeof(ActivityLibraryBaseResorces))]
        public bool IsEnabled { get; set; } = true;

        public RunScript()
        {
            System.Activities.Presentation.Metadata.AttributeTableBuilder builder = new System.Activities.Presentation.Metadata.AttributeTableBuilder();
            builder.AddCustomAttributes(typeof(RunScript), "Arguments",
                new EditorAttribute(typeof(ArgumentCollectionEditor2),
                typeof(System.Activities.Presentation.PropertyEditing.PropertyValueEditor)));
            System.Activities.Presentation.Metadata.MetadataStore.AddAttributeTable(builder.CreateTable());
            Arguments = new Dictionary<string, Argument>();
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (!IsEnabled)
            {
                return;
            }

            string pathToXamlActivity = PathToXamlActivity.Get(context);

            // Create a Bookmark and wait for it to be resumed.  
            _ = context.CreateBookmark(PathToXamlActivity.Get(context), new BookmarkCallback(OnResumeBookmark));
        }

        // NativeActivity derived activities that do asynchronous operations by calling
        // one of the CreateBookmark overloads defined on System.Activities.NativeActivityContext
        // must override the CanInduceIdle property and return true.  
        protected override bool CanInduceIdle => true;

        public void OnResumeBookmark(NativeActivityContext context, Bookmark bookmark, object obj)
        {
            // When the Bookmark is resumed, assign its value to  
            // the Result argument.  
            //Result.Set(context, (string)obj);
        }
    }
}
