using Activities.Browser.Designers;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using RpaCommon;
using System;
using System.Activities;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Activities.Browser
{
    [LocalizedDescription("create_tab_desc", typeof(Resources.ActivitiesBrowser))]
    [LocalizedDisplayName("create_tab", typeof(Resources.ActivitiesBrowser))]
    [Designer(typeof(CreateTabDesigner))]
    public sealed class CreateTab : TimeoutCodeActivity
    {
        [LocalizedDescription("browser_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("browser", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        [RequiredArgument]
        public InArgument<IBrowser> Browser { get; set; }

        [LocalizedDescription("start_url_desc", typeof(Resources.ActivitiesBrowser))]
        [DisplayName("Url")]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Url { get; set; }

        [LocalizedDescription("page_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IPage> Page { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IBrowser browser = Browser.Get(context);
            string url = context.GetValue(Url);

            IPage page = browser.NewPageAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(url))
            {
                _ = page.GoToAsync(url).GetAwaiter().GetResult(); ;
            }
            Page.Set(context, page);
        }

        private async Task<IPage> CreatePage(IBrowser browser, string url)
        {
            IPage page = await browser.NewPageAsync();
            if (!string.IsNullOrEmpty(url))
            {
                _ = await page.GoToAsync(url);
            }
            return page;
        }
    }
}
