using ActivityLibraryBase.BaseActivities;
using System;
using System.Activities;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using RpaCommon;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using Activities.Browser.Resources;

namespace Activities.Browser
{
    [LocalizedDescription("select_option_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("select_option", typeof(ActivitiesBrowser))]
    public sealed class SelectOption : TimeoutCodeActivity
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
        [LocalizedDescription("option_value_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("option_value", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Value { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string css = CSSSelector.Get(context);
            string xpath = XPathSelector.Get(context);
            string value = Value.Get(context);
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
                    ? SelectByXPathAsync(xpath, value, page)
                    : SelectByCSSAsync(css, value, page);
                    
                task.Wait();
                if (task.Result.Success)
                {
                    return;
                }
                ActivityLogger.Trace(task.Result.Error.Message);
                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult> SelectByCSSAsync(string css, string value, IPage page)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(css))
                {
                    return new CallResult { Error = new Error(new Exception("CSS-селектор пуст")) };
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    return new CallResult { Error = new Error(new Exception("Значение опции пусто")) };
                }

                string[] selected = await page.SelectAsync(css, value);
                if (selected == null || Array.IndexOf(selected, value) < 0)
                {
                    return new CallResult { Error = new Error(new Exception("Значение опции не выбрано")) };
                }

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }

        private async Task<CallResult> SelectByXPathAsync(string xpath, string value, IPage page)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(xpath))
                {
                    return new CallResult { Error = new Error(new Exception("XPath-селектор пуст")) };
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    return new CallResult { Error = new Error(new Exception("Значение опции пусто")) };
                }

                // Находим select элемент по XPath
                IElementHandle selectElement = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (selectElement == null)
                {
                    return new CallResult { Error = new Error(new Exception("Select элемент не найден по XPath")) };
                }

                // Выбираем опцию через JavaScript
                var result = await page.EvaluateFunctionAsync<string[]>(@"
                    (element, optionValue) => {
                        if (element.tagName.toLowerCase() !== 'select') {
                            throw new Error('Элемент не является select');
                        }
                        
                        element.value = optionValue;
                        element.dispatchEvent(new Event('change', { bubbles: true }));
                        
                        return [element.value];
                    }", selectElement, value);

                if (result == null || Array.IndexOf(result, value) < 0)
                {
                    return new CallResult { Error = new Error(new Exception("Значение опции не выбрано")) };
                }

                return new CallResult();
            }
            catch (Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }
    }
}
