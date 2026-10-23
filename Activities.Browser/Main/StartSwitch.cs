using Activities.Browser.Designers;
using Activities.Browser.Entities;
using Activities.Browser.Extensions;
using Activities.Browser.Helpers;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Helpers;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;

namespace Activities.Browser
{
    [LocalizedDescription("start_switch_profile_desc", typeof(Resources.ActivitiesBrowser))]
    [LocalizedDisplayName("start_switch_profile", typeof(Resources.ActivitiesBrowser))]
    [Designer(typeof(StartSwitchDesigner))]
    public sealed class StartSwitch : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("browser_profile_name_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("browser_profile_name", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> ProfileName { get; set; }

        [RequiredArgument]
        [LocalizedDescription("instal_switch_path_desc", typeof(Resources.ActivitiesBrowser))]
        [LocalizedDisplayName("instal_switch_path", typeof(Resources.ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> SwitchInstallPath { get; set; } = "C:\\MybotSwitchPro";

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
            string args = context.GetValue(Args);
            string profileName = context.GetValue(ProfileName);
            string url = context.GetValue(Url);
            bool headless = context.GetValue(Headless);
            string switchInstallPath = context.GetValue(SwitchInstallPath);

            string executablePath = Path.Combine(switchInstallPath, "chr", "chrome.exe");
            Profile profile = GetProfileFromProfileName(switchInstallPath, profileName);
#pragma warning disable IDE0270 // Use coalesce expression
            if (profile == null)
            {
                throw new ArgumentException(LocalizeHelper.Get("profile_exception", profileName, new ResourceManager(typeof(Resources.ActivitiesBrowser))));
            }
#pragma warning restore IDE0270 // Use coalesce expression
            string[] argsArray = args.GetArgsArray() ?? new string[] { };
            List<string> argsList = GetArgs(profile);
            argsList.AddRange(argsArray);
            argsList.Add($@"--user-data-dir={profile.ProfilePath}");
            LaunchOptions options = new LaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
                Timeout = 0,
                IgnoreDefaultArgs = true,
                Args = argsList.ToArray(),
                //UserDataDir = profile.ProfilePath,
                DefaultViewport = null
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

        private Profile GetProfileFromProfileName(string switchInstallPath, string profileName)
        {
            string pathToprofiles = Path.Combine(switchInstallPath, "profiles.json");
            if (!File.Exists(pathToprofiles))
            {
                throw new Exception($"Profiles file {pathToprofiles} not found");
            }
            List<Profile> profiles = ObjectHelper.InitObject<List<Profile>>(pathToprofiles);
            return profiles.FirstOrDefault(p => p.Title.Trim() == profileName);
        }

        private static List<string> GetArgs(Profile profile)
        {
            List<string> args = new List<string>
            {
                "--no-first-run",
                "--no-sandbox",
                "--test-type",
                "--remote-allow-origins=*"
            };

            //proxy & WebRTC settings
            if (profile.Proxy != null)
            {
                string proxyString;
                if (profile.Proxy.Login != null)
                {
                    proxyString = $"{profile.Proxy.Ip}:{profile.Proxy.Port}@{profile.Proxy.Login}:{profile.Proxy.Pwd}";
                    args.Add("--proxy-login=" + profile.Proxy.Login);
                    args.Add("--proxy_pwd=" + profile.Proxy.Pwd);
                    //TODO: сделать новый флаг ком стр
                    args.Add("--proxy-pwd=" + profile.Proxy.Pwd);
                }
                else
                {
                    proxyString = profile.Proxy.Ip + ":" + profile.Proxy.Port;
                }
                string proxyProtocol = profile.Proxy.ProxyProtocol.ToString().ToLower();

                Proxy proxy = proxyString != null && !string.IsNullOrWhiteSpace(proxyString) ? new Proxy(proxyString) : null;
                string proxyServer = proxy != null ? $"{proxy.Ip}:{proxy.Port}" : null;
                if (proxyServer != null)
                {
                    if (proxyProtocol == "http")
                    {
                        args.Add($"--proxy-server=http://{proxyServer}");
                    }

                    if (proxyProtocol == "socks5")
                    {
                        args.Add($"--proxy-server=\"socks5://{proxyServer}\"");
                    }
                }

                if (profile.WebRTCMode == WebRTCMode.ReplacementIP)
                {
                    args.Add("--webrtc-custom-mode=emulate");
                }
                else if (profile.WebRTCMode == WebRTCMode.DisableWebRTC)
                {
                    args.Add("--webrtc-ip-handling-policy=disable_non_proxied_udp");
                    args.Add("--force-webrtc-ip-handling-policy");
                }
            }
            else
            {
                if (profile.WebRTCMode == WebRTCMode.DisableWebRTC)
                {
                    args.Add("--webrtc-custom-mode=disable");
                }
            }

            //canvas rewrite settings
            if (profile.CanvasMode == CanvasMode.EditCanvas)
            {
                args.Add("--canvas-custom-mode=override");
            }
            else if (profile.CanvasMode == CanvasMode.DisableWebCanvas)
            {
                args.Add("--disable-reading-from-canvas");
            }

            //webgl
            if (profile.WebGLMode == WebGLMode.AddNoise)
            {
                args.Add("--webgl-custom-mode=override");
            }
            else if (profile.WebGLMode == WebGLMode.DisableWebGL)
            {
                args.Add("--disable-3d-apis");
                args.Add("--disable-webgl");
            }

            //audiocontext
            if (profile.AudiocontextMode == AudiocontextMode.AddNoise)
            {
                args.Add("--audio-custom-mode=override");
            }

            //TZ
            if (profile.TzMode == TZMode.OverrideHand)
            {
                string tz = profile.Tz?.UTC[0];
                if (tz != null)
                {
                    args.Add("--custom-tz=" + tz);
                }
            }

            //language
            if (profile.LocaleMode == LocaleMode.Override && profile.Locale != null)
            {
                args.Add("--lang=" + profile.Locale);
            }

            if (profile.AcceptLanguageMode == AcceptLanguageMode.Override && profile.AcceptLanguages.Count > 0)
            {
                args.Add("--accept-lang=" + string.Join(",", profile.AcceptLanguages));
            }

            //UserAgent
            if (!string.IsNullOrWhiteSpace(profile.UserAgent))
            {
                char ch = '"';
                string escapeStr = ch.ToString();
                foreach (char c in profile.UserAgent)
                {
                    if (c == ch)
                    {
                        escapeStr += "\\";
                    }
                    escapeStr += c.ToString();
                }
                escapeStr += ch.ToString();
                args.Add("--user-agent=" + escapeStr);
            }

            return args;
        }
    }
}
