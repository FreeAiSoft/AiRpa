using ActivityLibraryBase.BaseActivities;
using System.Activities;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using RpaCommon;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using Activities.Browser.Resources;
using ActivityLibraryBase.Helpers;

namespace Activities.Browser
{
    [LocalizedDescription("invoke_js_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("invoke_js", typeof(ActivitiesBrowser))]
    public sealed class InvokeJS : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        [RequiredArgument]
        [LocalizedDescription("script_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("script", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Script { get; set; }

        [LocalizedDescription("script_result_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("script_result", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<string> Result { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            string script = Script.Get(context);

            while (true)
            {
                Task<CallResult<string>> task = ExecuteScript(page, script);
                task.Wait();

                if (task.Result.Success)
                {
                    Result.Set(context, task.Result.Data);
                    return;
                }

                if (task.Result.Error != null)
                {
                    ActivityLogger.Trace(task.Result.Error.Message);
                }

                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult<string>> ExecuteScript(IPage page, string script)
        {
            try
            {
                // Try to evaluate as string directly
                string str = await page.EvaluateExpressionAsync<string>(script);
                return new CallResult<string> { Data = str };
            }
            catch (System.Exception)
            {
                try
                {
                    // Fallback: evaluate as object and stringify result in the page context
                    string json = await page.EvaluateFunctionAsync<string>(
                        @"(s) => {
                            try {
                                const r = eval(s);
                                if (typeof r === 'string') return r;
                                try { return JSON.stringify(r); } catch { return String(r); }
                            } catch (e) { return String(e); }
                        }",
                        script);
                    return new CallResult<string> { Data = json };
                }
                catch (System.Exception ex2)
                {
                    return new CallResult<string> { Error = new Error(ex2) };
                }
            }
        }
    }
}
