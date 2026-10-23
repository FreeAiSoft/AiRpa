using ActivityLibraryBase.BaseActivities;
using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System.Activities;
using System.Threading.Tasks;

namespace Activities.Browser
{
    [LocalizedDescription("refresh_page_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("refresh_page", typeof(ActivitiesBrowser))]
    public sealed class RefreshPage : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            Task task = Reload(page);
            task.Wait();
        }

        private async Task Reload(IPage page)
        {
            _ = await page.ReloadAsync();
        }
    }
}
