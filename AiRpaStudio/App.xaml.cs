using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Windows;
using RPA.Helpers;
using RPA.Mcp;
//#pragma warning disable
namespace RPA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static List<CultureInfo> Languages { get; } = new List<CultureInfo>();

        private RpaMcpServer _mcpServer;
        private StdioMcpServerHost _mcpStdioHost;
        private HttpMcpServerHost _mcpHttpHost;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Languages.Add(new CultureInfo("en-US")); //Нейтральная культура для этого проекта
            Languages.Add(new CultureInfo("ru-RU"));

            string lang = RPA.Properties.Settings.Default.Language;

            if (string.IsNullOrEmpty(lang))
            {
                //  автоматическая установка языка по культуре
                CultureInfo ci = CultureInfo.CurrentCulture;
                Language = new CultureInfo(ci.Name);
            }
            else
            {
                Language = new CultureInfo(lang == "RU" ? "ru-RU" : "en-US");
            }

            // Check if application is started in MCP server mode
            if (e.Args.Contains("--mcp-server"))
            {
                StartMcpServer(e.Args);
            }

            // Кисти Studio.* и системные кисти — реальными Brush из активной палитры MaterialDesign
            // (см. Helpers\StudioBrushes.cs: <DynamicResource x:Key="..."/> в словаре даёт
            // ResourceReferenceExpression и валит XamlParseException на MenuItem).
            StudioBrushes.ApplyToApplication();
            // Ключи дизайнера WF (F-05): объявляются до создания первого WorkflowDesigner, иначе
            // RoslynExpressionEditor.xaml не находит DesignerViewBackground и поле остаётся прозрачным.
            WfDesignerTheme.ApplyToApplication();

            // Скругление окон AvalonDock (план §7 M-03, W-06…W-08): окна ядра не ссылаются на сборку
            // студии, поэтому механизм скругления подставляется в них через мост.
            WindowCorner.EnableAvalonDockWindows();

            new EntryPoint().Run();
        }

        private void StartMcpServer(string[] args)
        {
            try
            {
                _mcpServer = new RpaMcpServer();

                // Check for server mode (stdio or http)
                bool useStdio = args.Contains("--stdio") || !args.Any(a => a.StartsWith("--port"));

                if (useStdio)
                {
                    // Start stdio server (for Claude Desktop and similar)
                    _mcpStdioHost = new StdioMcpServerHost(_mcpServer);
                    _mcpStdioHost.Start();
                    Console.WriteLine("MCP Server started in stdio mode");
                }
                else
                {
                    // Start HTTP server
                    int port = 3000; // Default port

                    // Parse port from arguments
                    string portArg = args.FirstOrDefault(a => a.StartsWith("--port"));
                    if (portArg != null)
                    {
                        string portValue = portArg.Split('=').Last();
                        if (int.TryParse(portValue, out int parsedPort))
                        {
                            port = parsedPort;
                        }
                    }

                    _mcpHttpHost = new HttpMcpServerHost(_mcpServer, port);
                    _mcpHttpHost.Start();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to start MCP Server: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Stop MCP servers if running
            _mcpStdioHost?.Stop();
            _mcpHttpHost?.Stop();

            base.OnExit(e);
        }

        //Евент для оповещения всех окон приложения
        public static event EventHandler LanguageChanged;

        public static CultureInfo Language
        {
            get => System.Threading.Thread.CurrentThread.CurrentUICulture;
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                if (value == System.Threading.Thread.CurrentThread.CurrentUICulture)
                {
                    return;
                }

                //1. Меняем язык приложения:
                System.Threading.Thread.CurrentThread.CurrentUICulture = value;

                //2. Создаём ResourceDictionary для новой культуры
                ResourceDictionary dict = new ResourceDictionary();
                ResourceManager resourceMan = new ResourceManager(typeof(Resources.Main));
                ResourceSet resourceSet = resourceMan.GetResourceSet(value, true, false);
                foreach (DictionaryEntry entry in resourceSet)
                {
                    dict.Add(entry.Key, entry.Value);
                }
                //ResourceDictionary dictProxyChecker = new ResourceDictionary();
                //switch (value.Name)
                //{
                //    case "ru-RU":
                //        //ResourceManager resourceMan = new ResourceManager(typeof(Resources.Main));
                //        dict.Source = new Uri(string.Format("Resources/Language.{0}.xaml", value.Name), UriKind.Relative);
                //        //dictProxyChecker.Source = new Uri(String.Format("pack://application:,,,/ProxyChecker;component/Resources/ProxyLanguage.{0}.xaml", value.Name), UriKind.Absolute);
                //        break;
                //    default:
                //        dict.Source = new Uri("Resources/Language.xaml", UriKind.Relative);
                //        //dictProxyChecker.Source = new Uri("pack://application:,,,/ProxyChecker;component/Resources/ProxyLanguage.xaml", UriKind.Absolute);
                //        break;
                //}

                //3. Находим старую ResourceDictionary и удаляем его и добавляем новую ResourceDictionary
                ResourceDictionary oldDict = Current.Resources.MergedDictionaries.FirstOrDefault(r => r.Contains("key_for_find_resource_main"));
                if (oldDict != null)
                {
                    int index = Current.Resources.MergedDictionaries.IndexOf(oldDict);
                    _ = Current.Resources.MergedDictionaries.Remove(oldDict);
                    Current.Resources.MergedDictionaries.Insert(index, dict);
                }
                else
                {
                    Current.Resources.MergedDictionaries.Add(dict);
                }

                //int indexProxyChecker = Application.Current.Resources.MergedDictionaries.IndexOf(oldDictProxyChecker);
                //Application.Current.Resources.MergedDictionaries.Remove(oldDictProxyChecker);
                //Application.Current.Resources.MergedDictionaries.Insert(indexProxyChecker, dictProxyChecker);
            }
        }
    }
}
