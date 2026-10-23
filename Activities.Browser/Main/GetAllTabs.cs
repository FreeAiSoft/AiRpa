using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System;
using System.Activities;

namespace Activities.Browser
{
    [LocalizedDescription("get_all_tabs_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("get_all_tabs", typeof(ActivitiesBrowser))]
    public sealed class GetAllTabs : TimeoutCodeActivity
    {
        [LocalizedDescription("browser_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("browser", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        [RequiredArgument]
        public InArgument<IBrowser> Browser { get; set; }

        [LocalizedDescription("pages_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("pages", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IPage[]> Pages { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            IBrowser browser = Browser.Get(context) ?? throw new ArgumentNullException(nameof(Browser));
            IPage[] pages = browser.PagesAsync().GetAwaiter().GetResult();
            Pages.Set(context, pages ?? Array.Empty<IPage>());
        }
    }
}
