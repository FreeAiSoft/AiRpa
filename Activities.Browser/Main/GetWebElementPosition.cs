using ActivityLibraryBase.BaseActivities;
using System;
using System.Activities;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using RpaCommon;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Resources;
using ActivityLibraryBase.Helpers;
using Activities.Browser.Resources;

namespace Activities.Browser
{
    [LocalizedDescription("get_web_element_position_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("get_web_element_position", typeof(ActivitiesBrowser))]
    public sealed class GetWebElementPosition : TimeoutCodeActivity
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

        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<double> X { get; set; }

        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<double> Y { get; set; }

        [LocalizedDescription("width_web_element_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("width", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<double> Width { get; set; }

        [LocalizedDescription("height_web_element_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("height", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<double> Height { get; set; }

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
                Task<CallResult<BoundingBox>> task = useXPath
                    ? GetBoundingBoxByXPath(xpath, page)
                    : GetBoundingBoxByCSS(css, page);
                    
                task.Wait();

                var result = task.Result;
                if (result.Success && result.Data != null)
                {
                    var box = result.Data;
                    X.Set(context, box.X);
                    Y.Set(context, box.Y);
                    Width.Set(context, box.Width);
                    Height.Set(context, box.Height);
                    return;
                }

                if (result.Error != null)
                {
                    ActivityLogger.Trace(result.Error.Message);
                }

                Thread.Sleep(1000);
            }
        }

        private async Task<CallResult<BoundingBox>> GetBoundingBoxByCSS(string css, IPage page)
        {
            try
            {
                IElementHandle element = await page.QuerySelectorAsync(css);
                if (element == null)
                {
                    return new CallResult<BoundingBox>();
                }

                BoundingBox box = await element.BoundingBoxAsync();
                return box != null ? new CallResult<BoundingBox> { Data = box } : new CallResult<BoundingBox>();
            }
            catch (Exception ex)
            {
                return new CallResult<BoundingBox> { Error = new Error(ex) };
            }
        }

        private async Task<CallResult<BoundingBox>> GetBoundingBoxByXPath(string xpath, IPage page)
        {
            try
            {
                IElementHandle element = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (element == null)
                {
                    return new CallResult<BoundingBox>();
                }

                BoundingBox box = await element.BoundingBoxAsync();
                return box != null ? new CallResult<BoundingBox> { Data = box } : new CallResult<BoundingBox>();
            }
            catch (Exception ex)
            {
                return new CallResult<BoundingBox> { Error = new Error(ex) };
            }
        }
    }
}
