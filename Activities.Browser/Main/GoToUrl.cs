using ActivityLibraryBase.BaseActivities;
using System.Activities;
using PuppeteerSharp;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using Activities.Browser.Resources;

namespace Activities.Browser
{
    [LocalizedDescription("go_to_url", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("go_to_url", typeof(ActivitiesBrowser))]
    public sealed class GoToUrl : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        [RequiredArgument]
        [LocalizedDescription("url_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("url", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Url { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            string url = Url.Get(context);
            _ = page.GoToAsync(url).GetAwaiter().GetResult();
        }
    }
}
