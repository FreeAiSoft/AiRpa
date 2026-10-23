//using System.Activities;
//using System.ComponentModel;

namespace Activities.Flow
{
    //[Description("Запуск активности")]
    //[Designer(typeof(RunScriptDesigner))]
    //public sealed class RunScript : NativeActivity
    //{
    //    [Description("Путь к активности")]
    //    public InArgument<string> PathToXamlActivity { get; set; }

    //    [Category("Общие")]
    //    [Description("Включено")]
    //    public InArgument<bool> IsEnabled { get; set; } = true;

    //    [Category("Общие")]
    //    [Description("Логгировать")]
    //    public InArgument<bool> IsLogging { get; set; } = true;

    //    protected override void Execute(NativeActivityContext context)
    //    {
    //        if (!IsEnabled.Get(context))
    //        {
    //            return;
    //        }

    //        bool isLogging = IsLogging.Get(context);
    //        string pathToXamlActivity = PathToXamlActivity.Get(context);

    //        ActivityLogger.Info($"{pathToXamlActivity}", isLogging);

    //        // Create a Bookmark and wait for it to be resumed.  
    //        _ = context.CreateBookmark(PathToXamlActivity.Get(context), new BookmarkCallback(OnResumeBookmark));

    //        //context.Track(new System.Activities.Tracking.CustomTrackingRecord("hello!!!"));
    //    }

    //    // NativeActivity derived activities that do asynchronous operations by calling
    //    // one of the CreateBookmark overloads defined on System.Activities.NativeActivityContext
    //    // must override the CanInduceIdle property and return true.  
    //    protected override bool CanInduceIdle => true;

    //    public void OnResumeBookmark(NativeActivityContext context, Bookmark bookmark, object obj)
    //    {
    //        // When the Bookmark is resumed, assign its value to  
    //        // the Result argument.  
    //        //Result.Set(context, (string)obj);
    //    }
    //}
}
