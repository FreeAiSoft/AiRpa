using ActivityLibraryBase.BaseActivities;
using System.Activities;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using RpaCommon;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using Activities.Browser.Resources;
using System;

namespace Activities.Browser
{
    [LocalizedDescription("input_text_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("input_text", typeof(ActivitiesBrowser))]
    public sealed class InputText : TimeoutCodeActivity
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
        [LocalizedDescription("text_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("text", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Text { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string css = CSSSelector.Get(context);
            string xpath = XPathSelector.Get(context);
            string text = Text.Get(context);
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
                    ? TypeIntoByXPath(xpath, text, page)
                    : TypeIntoByCSS(css, text, page);
                    
                task.Wait();

                if (task.Result.Success)
                {
                    return;
                }

                ActivityLogger.Trace(task.Result.Error.Message);
                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult> TypeIntoByCSS(string css, string text, IPage page)
        {
            try
            {
                IElementHandle element = await page.QuerySelectorAsync(css);
                if (element == null)
                {
                    return new CallResult { Error = new Error(new System.Exception("Элемент не найден")) };
                }

                await element.FocusAsync();

                // Очищаем существующее значение для input/textarea
                await page.EvaluateFunctionAsync(@"e => {
                    if (e instanceof HTMLInputElement || e instanceof HTMLTextAreaElement) {
                        e.value = '';
                        e.dispatchEvent(new Event('input', { bubbles: true }));
                        e.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                }", element);

                await element.TypeAsync(text ?? string.Empty);
                return new CallResult();
            }
            catch (System.Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }

        private async Task<CallResult> TypeIntoByXPath(string xpath, string text, IPage page)
        {
            try
            {
                IElementHandle element = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (element == null)
                {
                    return new CallResult { Error = new Error(new System.Exception("Элемент не найден по XPath")) };
                }

                await element.FocusAsync();

                // Очищаем существующее значение для input/textarea
                await page.EvaluateFunctionAsync(@"e => {
                    if (e instanceof HTMLInputElement || e instanceof HTMLTextAreaElement) {
                        e.value = '';
                        e.dispatchEvent(new Event('input', { bubbles: true }));
                        e.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                }", element);

                await element.TypeAsync(text ?? string.Empty);
                return new CallResult();
            }
            catch (System.Exception ex)
            {
                return new CallResult { Error = new Error(ex) };
            }
        }
    }
}
