using ActivityLibraryBase.BaseActivities;
using System;
using System.Activities;
using System.Threading;
using System.Threading.Tasks;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using Activities.Browser.Resources;
using PuppeteerSharp;
using RpaCommon;

namespace Activities.Browser
{
    [LocalizedDescription("switch_tab_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("switch_tab", typeof(ActivitiesBrowser))]
    public sealed class SwitchTab : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("browser_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("browser", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IBrowser> Browser { get; set; }

        [LocalizedDescription("tab_index_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("tab_index", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<int> Index { get; set; }

        [LocalizedDescription("tab_title_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("tab_title", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Title { get; set; }

        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IPage> Page { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IBrowser browser = Browser.Get(context);
            bool hasIndex = Index.Expression != null;
            int index = hasIndex ? Index.Get(context) : -1;
            string title = Title.Get(context);

            while (true)
            {
                Task<CallResult<IPage>> task = SwitchAsync(browser, hasIndex, index, title);
                task.Wait();

                if (task.Result.Success && task.Result.Data != null)
                {
                    Page.Set(context, task.Result.Data);
                    return;
                }

                if (task.Result.Error != null)
                {
                    ActivityLogger.Trace(task.Result.Error.Message);
                }

                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult<IPage>> SwitchAsync(IBrowser browser, bool hasIndex, int index, string title)
        {
            try
            {
                if (browser == null)
                    return new CallResult<IPage> { Error = new Error(new ArgumentNullException(nameof(browser))) };

                var pages = await browser.PagesAsync();
                if (pages == null || pages.Length == 0)
                    return new CallResult<IPage> { Error = new Error(new Exception("No pages found")) };

                IPage target = null;

                if (hasIndex)
                {
                    if (index < 0 || index >= pages.Length)
                        return new CallResult<IPage> { Error = new Error(new ArgumentOutOfRangeException(nameof(index))) };

                    target = pages[index];
                }
                else if (!string.IsNullOrWhiteSpace(title))
                {
                    foreach (var p in pages)
                    {
                        string t = await p.GetTitleAsync();
                        if (string.Equals(t, title, StringComparison.OrdinalIgnoreCase))
                        {
                            target = p;
                            break;
                        }
                    }

                    if (target == null)
                    {
                        foreach (var p in pages)
                        {
                            string t = await p.GetTitleAsync();
                            if (!string.IsNullOrEmpty(t) && t.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                target = p;
                                break;
                            }
                        }
                    }

                    if (target == null)
                        return new CallResult<IPage> { Error = new Error(new Exception("Tab by title not found")) };
                }
                else
                {
                    return new CallResult<IPage> { Error = new Error(new Exception("Neither index nor title provided")) };
                }

                await target.BringToFrontAsync();
                return new CallResult<IPage> { Data = target };
            }
            catch (Exception ex)
            {
                return new CallResult<IPage> { Error = new Error(ex) };
            }
        }
    }
}
