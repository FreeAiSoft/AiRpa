using Activities.Browser.Designers;
using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Activities.Browser
{
    [LocalizedDescription("find_web_elements_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("find_web_elements", typeof(ActivitiesBrowser))]
    [Designer(typeof(FindWebElementsDesigner))]
    public sealed class FindWebElements : TimeoutCodeActivity
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

        [LocalizedDescription("web_elements_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("web_elements", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IElementHandle[]> Elements { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context) ?? throw new ArgumentNullException(nameof(Page));
            string cssSelector = CSSSelector.Get(context);
            string xpathSelector = XPathSelector.Get(context);

            // Проверяем, что заполнен хотя бы один селектор
            if (string.IsNullOrWhiteSpace(cssSelector) && string.IsNullOrWhiteSpace(xpathSelector))
            {
                throw new ArgumentException("Необходимо указать CSS-селектор или XPath-селектор");
            }

            // Если заполнены оба селектора, приоритет у CSS
            bool useXPath = string.IsNullOrWhiteSpace(cssSelector) && !string.IsNullOrWhiteSpace(xpathSelector);

            IElementHandle[] elements;
            if (useXPath)
            {
                elements = FindElementsByXPath(xpathSelector, page).GetAwaiter().GetResult();
            }
            else
            {
                elements = page.QuerySelectorAllAsync(cssSelector).GetAwaiter().GetResult();
            }

            Elements.Set(context, elements ?? Array.Empty<IElementHandle>());
        }

        private async Task<IElementHandle[]> FindElementsByXPath(string xpath, IPage page)
        {
            try
            {
                // Используем более простой подход через JavaScript, который создает селектор
                // и возвращает массив найденных элементов
                var elementHandles = await page.EvaluateFunctionAsync<IElementHandle[]>(@"
                    (xpath) => {
                        const iterator = document.evaluate(xpath, document, null, XPathResult.ORDERED_NODE_ITERATOR_TYPE, null);
                        const elements = [];
                        let node = iterator.iterateNext();
                        while (node) {
                            elements.push(node);
                            node = iterator.iterateNext();
                        }
                        return elements;
                    }", xpath);

                return elementHandles ?? Array.Empty<IElementHandle>();
            }
            catch (Exception)
            {
                // Если JavaScript подход не работает, попробуем через цикл поиска отдельных элементов
                try
                {
                    var result = new List<IElementHandle>();
                    int index = 1;
                    
                    // Ограничиваем поиск разумным количеством элементов
                    while (index <= 100)
                    {
                        string indexedXPath = $"({xpath})[{index}]";
                        
                        try
                        {
                            var element = await page.WaitForXPathAsync(indexedXPath, new WaitForSelectorOptions { Timeout = 100 });
                            if (element != null)
                            {
                                result.Add(element);
                                index++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (TimeoutException)
                        {
                            break;
                        }
                    }
                    
                    return result.ToArray();
                }
                catch (Exception)
                {
                    return Array.Empty<IElementHandle>();
                }
            }
        }
    }
}
