using System.Activities;
using System.ComponentModel;
using System.Threading.Tasks;
using Activities.Browser.Designers;
using Activities.Browser.Extensions;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;

namespace Activities.Browser
{
    [LocalizedDescription("start_browser_desc", typeof(Resources.ActivitiesBrowser))]
    [LocalizedDisplayName("start_browser", typeof(Resources.ActivitiesBrowser))]
    [Designer(typeof(StartBrowserDesigner))]
    public sealed class StartBrowser : TimeoutCodeActivity
    {
        [LocalizedDescription("user_data_dir_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("user_data_dir", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> UserDataDir { get; set; }

        [LocalizedDescription("executable_path_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("executable_path", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        [RequiredArgument]
        public InArgument<string> ExecutablePath { get; set; }

        [LocalizedDescription("start_url_desc", typeof(Resources.ActivitiesBrowser))]
        [DisplayName("Url")]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Url { get; set; }

        [LocalizedDescription("args_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("args", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Args { get; set; }

        [LocalizedDescription("headless_mode_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("headless_mode", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<bool> Headless { get; set; }

        //[Description("Удалить куки")]
        //public InArgument<bool> NeedDeleteCookies { get; set; } = true;

        //[Category("View port")]
        //[Description("Ширина")]
        //public InArgument<int> Width { get; set; } = 1920;

        //[Category("View port")]
        //[Description("Высота")]
        //public InArgument<int> Height { get; set; } = 1080;

        //[Description("Таймаут ожидания загрузки страницы (мс)")]
        //public InArgument<int> Timeout { get; set; } = 30000;

        [LocalizedDescription("browser_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("browser", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IBrowser> Browser { get; set; }

        [LocalizedDescription("first_page_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("first_page", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<IPage> Page { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            Task task = Task.Run(() => Go(context));
            task.Wait();
        }

        private async Task Go(CodeActivityContext context)
        {
            string executablePath = context.GetValue(ExecutablePath);
            string args = context.GetValue(Args);
            string userDataDir = context.GetValue(UserDataDir);
            string url = context.GetValue(Url);
            bool headless = context.GetValue(Headless);

            LaunchOptions options = new LaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
                Timeout = 0,
                IgnoreDefaultArgs = false,
                Args = args.GetArgsArray() ?? new string[] { },
                UserDataDir = userDataDir
            };

            IBrowser browser = await Puppeteer.LaunchAsync(options);
            IPage[] pages = await browser.PagesAsync();

            Browser.Set(context, browser);
            Page.Set(context, pages[0]);

            if (!string.IsNullOrEmpty(url))
            {
                _ = await pages[0].GoToAsync(url);
            }
        }
    }
}
