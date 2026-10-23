using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using Microsoft.Windows.Design;
using PuppeteerSharp;
using System.Activities;
using System.Threading.Tasks;

namespace Activities.Browser
{
    [LocalizedDescription("back_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("back", typeof(ActivitiesBrowser))]
    [ToolboxBrowsable(true)]
    public sealed class Back : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            Task task = GoBack(page);
            task.Wait();
        }

        private async Task GoBack(IPage page)
        {
            _ = await page.GoBackAsync();
        }
    }
}
