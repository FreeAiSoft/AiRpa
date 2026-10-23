using System;
using System.Activities;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Activities.Browser.Designers;
using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using RpaCommon;

namespace Activities.Browser
{
    [Designer(typeof(ClickWebElementDesigner))]
    [LocalizedDescription("web_element_click_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("web_element_click", typeof(ActivitiesBrowser))]
    public sealed class ClickWebElement : TimeoutCodeActivity
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
                Task<CallResult> task = useXPath
                    ? ClickByXPath(xpath, page)
                    : ClickByCSS(css, page);
                    
                task.Wait();
                if (task.Result.Success)
                {
                    return;
                }
                ActivityLogger.Trace(task.Result.Error.Message);
                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult> ClickByCSS(string css, IPage page)
        {
            try
            {
                await page.ClickAsync(css);
                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }

        private async Task<CallResult> ClickByXPath(string xpath, IPage page)
        {
            try
            {
                IElementHandle element = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (element != null)
                {
                    await element.ClickAsync();
                    return new CallResult();
                }
                return new CallResult { Error = new Error(new Exception("Элемент не найден по XPath")) };
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }
    }
}
