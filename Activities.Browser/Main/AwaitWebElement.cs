using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System;
using System.Activities;
using System.Threading.Tasks;
using System.Threading;
using RpaCommon;
using Activities.Browser.Designers;
using System.ComponentModel;
using Microsoft.Windows.Design;

namespace Activities.Browser
{
    [LocalizedDescription("await_web_element_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("await_web_element", typeof(ActivitiesBrowser))]
    [Designer(typeof(AwaitWebElementDesigner))]
    [ToolboxBrowsable(true)]
    [ToolboxItem(true)]
    public sealed class AwaitWebElement : TimeoutCodeActivity
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

        [LocalizedDescription("web_element_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("web_element", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IElementHandle> Element { get; set; }

        [LocalizedDescription("is_element_exist_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("is_element_exist", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<bool> IsElementExist { get; set; }

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
                Task<CallResult<IElementHandle>> task = useXPath
                    ? FindElementByXPath(xpath, page)
                    : FindElementByCSS(css, page);

                task.Wait();
                if (task.Result.Success && task.Result.Data != null)
                {
                    Element.Set(context, task.Result.Data);
                    IsElementExist.Set(context, true);
                    return;
                }
                else
                {
                    ActivityLogger.Trace(task.Result.Error.Message);
                }

                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult<IElementHandle>> FindElementByCSS(string css, IPage page)
        {
            try
            {
                IElementHandle element = await page.QuerySelectorAsync(css);
                return element != null ? new CallResult<IElementHandle> { Data = element } : new CallResult<IElementHandle>();
            }
            catch (Exception ex)
            {
                return new CallResult<IElementHandle> { Error = new Error(ex) };
            }
        }

        private async Task<CallResult<IElementHandle>> FindElementByXPath(string xpath, IPage page)
        {
            try
            {
                // Используем WaitForXPathAsync с минимальным таймаутом для проверки существования
                IElementHandle element = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                return element != null ? new CallResult<IElementHandle> { Data = element } : new CallResult<IElementHandle>();
            }
            catch (TimeoutException)
            {
                // Элемент не найден, но это не ошибка для AwaitWebElement
                return new CallResult<IElementHandle>();
            }
            catch (Exception ex)
            {
                return new CallResult<IElementHandle> { Error = new Error(ex) };
            }
        }
    }
}
