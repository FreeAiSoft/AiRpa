using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Windows;
using RPA.Helpers;
using RPA.Logging;
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

        // ВРЕМЕННО (диагностика замерзаний интерфейса): ловим имя сборки, которую CLR не может привязать.
        private static readonly HashSet<string> _loggedLoadFailures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object _loadFailureLogLock = new object();

        public App()
        {
            // Обработчики падений подключаются ДО InitializeComponent/Startup: ошибка в App.xaml,
            // в ресурсах темы или при создании главного окна не должна приводить к молчаливому
            // закрытию (раньше подписка жила в EntryPoint, т.е. уже после разбора App.xaml).
            // Падения UI-потока WPF ловит DispatcherUnhandledException — WinForms-событие
            // Application.ThreadException для WPF-приложения не срабатывает.
            AppDomain.CurrentDomain.UnhandledException += Log.AppDomain_CurrentDomain_UnhandledException;
            DispatcherUnhandledException += Log.Application_DispatcherUnhandledException;

            // Страховка от несогласованных версий зависимостей: WF-панель «Пространства имён»
            // перечисляет типы сборок (Assembly.GetExportedTypes), и копия библиотеки другой версии
            // в папке приложения роняла дизайнер модальным окном FileLoadException (HRESULT 0x80131040).
            AssemblyBindingUnifier.Install(AppDomain.CurrentDomain);

            // ВРЕМЕННО (диагностика замерзаний интерфейса): first-chance FileLoadException -> имя сборки и стек.
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

            StartupTimer.Mark("App: конструктор завершён (обработчики падений + AssemblyBindingUnifier)");
        }

        // ВРЕМЕННО (диагностика замерзаний интерфейса): first-chance FileLoadException -> имя сборки и стек.
        private void OnFirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                if (!(e?.Exception is System.IO.FileLoadException fle))
                {
                    return;
                }

                // Одинаковые причины (одна и та же сборка) логируем один раз, чтобы не заспамить лог.
                string key = (fle.Message ?? string.Empty) + " | " + (fle.FileName ?? string.Empty);
                bool first;
                lock (_loadFailureLogLock)
                {
                    first = _loggedLoadFailures.Add(key);
                }

                if (!first)
                {
                    return;
                }

                string fusion = string.Empty;
                try { fusion = fle.FusionLog; } catch { }

                System.Diagnostics.Debug.WriteLine("[FileLoadException-DIAG] " + fle.Message);
                System.Diagnostics.Debug.WriteLine("[FileLoadException-DIAG] FileName=" + fle.FileName);
                System.Diagnostics.Debug.WriteLine("[FileLoadException-DIAG] FusionLog=" + fusion);
                System.Diagnostics.Debug.WriteLine("[FileLoadException-DIAG] Stack:" + Environment.NewLine + Environment.StackTrace);

                try
                {
                    Log.Write("[FileLoadException-DIAG] " + fle.Message
                              + " | FileName=" + fle.FileName
                              + " | FusionLog=" + fusion);
                    Log.Write("[FileLoadException-DIAG] Stack:" + Environment.NewLine + Environment.StackTrace);
                }
                catch
                {
                    // Лог может быть не готов на раннем этапе — это нормально.
                }
            }
            catch
            {
                // Обработчик диагностики никогда не должен влиять на работу приложения.
            }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                StartupCore(e);
            }
            catch (Exception ex)
            {
                // Ошибка инициализации (тема, WF-дизайнер, главное окно): окно ошибки и выход.
                Log.ReportExceptionAndShowMessage(ex);
                Shutdown();
            }
        }

        private void StartupCore(StartupEventArgs e)
        {
            StartupTimer.Mark("StartupCore: начало (после разбора App.xaml)");
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

            StartupTimer.Mark("StartupCore: язык и культуры установлены");

            // Check if application is started in MCP server mode
            if (e.Args.Contains("--mcp-server"))
            {
                StartMcpServer(e.Args);
                StartupTimer.Mark("StartupCore: MCP-сервер запущен");
            }

            // Кисти Studio.* и системные кисти — реальными Brush из активной палитры MaterialDesign
            // (см. Helpers\StudioBrushes.cs: <DynamicResource x:Key="..."/> в словаре даёт
            // ResourceReferenceExpression и валит XamlParseException на MenuItem).
            StudioBrushes.ApplyToApplication();
            StartupTimer.Mark("StartupCore: StudioBrushes.ApplyToApplication()");
            // Шрифт Roboto для всего проекта (этап 12): ключи Studio.FontFamily* читают XAML,
            // тема AvalonDock и сторонние сборки (лог, чат).
            StudioFonts.ApplyToApplication();
            StartupTimer.Mark("StartupCore: StudioFonts.ApplyToApplication()");
            // Ключи дизайнера WF (F-05): объявляются до создания первого WorkflowDesigner, иначе
            // RoslynExpressionEditor.xaml не находит DesignerViewBackground и поле остаётся прозрачным.
            WfDesignerTheme.ApplyToApplication();
            StartupTimer.Mark("StartupCore: WfDesignerTheme.ApplyToApplication()");
            // Цвета WF — подменой словаря ColorResources.xaml в кэше шаблонов (F-05): установка обязана
            // пройти ДО первого WorkflowDesigner/DesignerMetadata().Register(), иначе первый кадр
            // активности рисуется дефолтами WF и видно мерцание (Helpers\WfColorResourcesOverride.cs).
            WfColorResourcesOverride.TryInstall(StudioPalette.Active);
            StartupTimer.Mark("StartupCore: WfColorResourcesOverride.TryInstall()");

            // Скругление окон AvalonDock (план §7 M-03, W-06…W-08): окна ядра не ссылаются на сборку
            // студии, поэтому механизм скругления подставляется в них через мост.
            WindowCorner.EnableAvalonDockWindows();
            StartupTimer.Mark("StartupCore: WindowCorner.EnableAvalonDockWindows()");

            new EntryPoint().Run();
            StartupTimer.Mark("StartupCore: EntryPoint.Run() завершён");
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
                Console.Error.WriteLine($"Failed to start MCP Server: {ex}");
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
