using System;
using System.Activities;
using System.ComponentModel;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Activities.Browser.Designers;
using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Helpers;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using RpaCommon;

namespace Activities.Browser
{
    [LocalizedDescription("get_text_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("get_text", typeof(ActivitiesBrowser))]
    [Designer(typeof(GetTextDesigner))]
    public sealed class GetTextFromWebElement : TimeoutCodeActivity
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

        [LocalizedDescription("xpath_selector_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("xpath_selector", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> XPathSelector { get; set; }

        [RequiredArgument]
        [LocalizedDescription("text_in_element_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("text_in_element", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<string> Text { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string css = CSSSelector.Get(context);
            string xpath = XPathSelector.Get(context);
            IPage page = Page.Get(context);

            // Проверяем, что заполнен хотя бы один селектор
            if (string.IsNullOrWhiteSpace(css) && string.IsNullOrWhiteSpace(xpath))
            {
                throw new ArgumentException("Необходимо указать CSS-селектор или XPath-селектор");
            }

            // Если заполнены оба селектора, приоритет у CSS
            bool useXPath = string.IsNullOrWhiteSpace(css) && !string.IsNullOrWhiteSpace(xpath);

            while (true)
            {
                Task<CallResult<string>> task = useXPath
                    ? GetTextByXPath(xpath, page)
                    : GetTextByCSS(css, page);

                task.Wait();
                if (task.Result.Success)
                {
                    Text.Set(context, task.Result.Data);
                    return;
                }
                ActivityLogger.Trace(task.Result.Error.Message);
                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult<string>> GetTextByCSS(string css, IPage page)
        {
            IElementHandle elementHandle = await page.QuerySelectorAsync(css);
            try
            {
                string content = await page.EvaluateFunctionAsync<string>("e => e.textContent", elementHandle);
                return content == null
                    ? new CallResult<string> { Error = new Error(LocalizeHelper.Get("get_text_error", new ResourceManager(typeof(ActivitiesBrowser)))) }
                    : new CallResult<string> { Data = content };
            }
            catch (Exception ex)
            {
                return new CallResult<string> { Error = new Error(ex) };
            }
        }

        private async Task<CallResult<string>> GetTextByXPath(string xpath, IPage page)
        {
            try
            {
                IElementHandle elementHandle = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (elementHandle == null)
                {
                    return new CallResult<string> { Error = new Error(new Exception("Элемент не найден по XPath")) };
                }

                string content = await page.EvaluateFunctionAsync<string>("e => e.textContent", elementHandle);
                return content == null
                    ? new CallResult<string> { Error = new Error(LocalizeHelper.Get("get_text_error", new ResourceManager(typeof(ActivitiesBrowser)))) }
                    : new CallResult<string> { Data = content };
            }
            catch (Exception ex)
            {
                return new CallResult<string> { Error = new Error(ex) };
            }
        }
    }
}
