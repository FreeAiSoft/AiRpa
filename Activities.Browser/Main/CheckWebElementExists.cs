using Activities.Browser.Designers;
using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System;
using System.Activities;
using System.ComponentModel;
using System.Threading.Tasks;
using RpaCommon;

namespace Activities.Browser
{
    [LocalizedDescription("check_web_element_exist_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("check_web_element_exist", typeof(ActivitiesBrowser))]
    [Designer(typeof(CheckWebElementExistsDesigner))]
    public sealed class CheckWebElementExists : BaseCodeActivity
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
        [LocalizedDescription("is_element_exist_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("is_element_exist", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<bool> IsElementExist { get; set; }

        public override void Run(CodeActivityContext context)
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

            Task<CallResult<bool>> task = useXPath
                ? FindElementByXPath(xpath, page)
                : FindElementByCSS(css, page);
                
            task.Wait();
            if (task.Result.Success && task.Result.Data)
            {
                IsElementExist.Set(context, true);
            }
            else
            {
                IsElementExist.Set(context, false);
                if (task.Result.Error != null)
                {
                    ActivityLogger.Trace(task.Result.Error.Message);
                }
            }
        }

        private async Task<CallResult<bool>> FindElementByCSS(string css, IPage page)
        {
            try
            {
                IElementHandle element = await page.QuerySelectorAsync(css);
                return element != null ? new CallResult<bool> { Data = true } : new CallResult<bool>();
            }
            catch (Exception ex)
            {
                return new CallResult<bool> { Error = new Error(ex) };
            }
        }

        private async Task<CallResult<bool>> FindElementByXPath(string xpath, IPage page)
        {
            try
            {
                IElementHandle element = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                return element != null ? new CallResult<bool> { Data = true } : new CallResult<bool>();
            }
            catch (TimeoutException)
            {
                // Элемент не найден, возвращаем false без ошибки
                return new CallResult<bool> { Data = false };
            }
            catch (Exception ex)
            {
                return new CallResult<bool> { Error = new Error(ex) };
            }
        }
    }
}
