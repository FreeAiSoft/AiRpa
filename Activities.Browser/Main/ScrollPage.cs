using System;
using System.Activities;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using ActivityLibraryBase.Helpers;
using Activities.Browser.Resources;
using PuppeteerSharp;
using RpaCommon;

namespace Activities.Browser
{
    [LocalizedDescription("scroll_page_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("scroll_page", typeof(ActivitiesBrowser))]
    public sealed class ScrollPage : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        [LocalizedDescription("css_selector_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("css_selector", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> CSSSelector { get; set; }

        [DisplayName("DeltaX")]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<int> DeltaX { get; set; }

        [DisplayName("DeltaY")]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<int> DeltaY { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            string css = CSSSelector?.Get(context);
            int dx = DeltaX.Expression != null ? DeltaX.Get(context) : 0;
            int dy = DeltaY.Expression != null ? DeltaY.Get(context) : 0;

            while (true)
            {
                Task<CallResult> task = Scroll(page, css, dx, dy);
                task.Wait();

                if (task.Result.Success)
                {
                    return;
                }

                if (task.Result.Error != null)
                {
                    ActivityLogger.Trace(task.Result.Error.Message);
                }

                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult> Scroll(IPage page, string css, int dx, int dy)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(css))
                {
                    IElementHandle element = await page.QuerySelectorAsync(css);
                    if (element == null)
                    {
                        return new CallResult { Error = new Error(new Exception("Element not found")) };
                    }

                    await page.EvaluateFunctionAsync(
                        @"(e) => {
                            try { e.scrollIntoView({ behavior: 'auto', block: 'center', inline: 'nearest' }); }
                            catch { e.scrollIntoView(true); }
                        }",
                        element);

                    return new CallResult();
                }
                else
                {
                    await page.EvaluateFunctionAsync(
                        "(dx, dy) => { window.scrollBy(dx || 0, dy || 0); }",
                        dx, dy);

                    return new CallResult();
                }
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }
    }
}
