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
    [Designer(typeof(GetWebElementAttributeDesigner))]
    [LocalizedDescription("get_attribute_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("get_attribute", typeof(ActivitiesBrowser))]
    public sealed class GetWebElementAttribute : TimeoutCodeActivity
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
        [LocalizedDescription("attribute_name_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("attribute_name", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> AttributeName { get; set; }

        [RequiredArgument]
        [LocalizedDescription("attribute_value_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("attribute_value", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<string> AttributeValue { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string css = CSSSelector.Get(context);
            string xpath = XPathSelector.Get(context);
            string attrName = AttributeName.Get(context);
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
                    ? GetAttributeByXPath(xpath, attrName, page)
                    : GetAttributeByCSS(css, attrName, page);

                task.Wait();
                if (task.Result.Success)
                {
                    AttributeValue.Set(context, task.Result.Data);
                    return;
                }
                ActivityLogger.Trace(task.Result.Error.Message);
                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult<string>> GetAttributeByCSS(string css, string attributeName, IPage page)
        {
            IElementHandle elementHandle = await page.QuerySelectorAsync(css);
            try
            {
                string value = await page.EvaluateFunctionAsync<string>("(e, a) => e.getAttribute(a)", elementHandle, attributeName);
                return value == null
                    ? new CallResult<string> { Error = new Error(LocalizeHelper.Get("get_attribute_error", new ResourceManager(typeof(ActivitiesBrowser)))) }
                    : new CallResult<string> { Data = value };
            }
            catch (Exception ex)
            {
                return new CallResult<string> { Error = new Error(ex) };
            }
        }

        private async Task<CallResult<string>> GetAttributeByXPath(string xpath, string attributeName, IPage page)
        {
            try
            {
                IElementHandle elementHandle = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (elementHandle == null)
                {
                    return new CallResult<string> { Error = new Error(new Exception("Элемент не найден по XPath")) };
                }

                string value = await page.EvaluateFunctionAsync<string>("(e, a) => e.getAttribute(a)", elementHandle, attributeName);
                return value == null
                    ? new CallResult<string> { Error = new Error(LocalizeHelper.Get("get_attribute_error", new ResourceManager(typeof(ActivitiesBrowser)))) }
                    : new CallResult<string> { Data = value };
            }
            catch (Exception ex)
            {
                return new CallResult<string> { Error = new Error(ex) };
            }
        }
    }
}
