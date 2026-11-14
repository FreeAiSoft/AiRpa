using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AiRpaStudio.Tests.Design;
using RPA.Controls;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Панель «Активности»: состояние узлов-категорий (свёрнуто/развёрнуто) персистится в настройку
/// <c>ActivityToolboxLayout</c> и восстанавливается при старте и пересборке палитры.
/// </summary>
[Apartment(ApartmentState.STA)]
public class ToolboxCategoryStateTests
{
    private string _savedActivityToolboxLayout;

    [SetUp]
    public void SetUp()
    {
        _savedActivityToolboxLayout = RPA.Properties.Settings.Default.ActivityToolboxLayout;
    }

    [TearDown]
    public void TearDown()
    {
        RPA.Properties.Settings.Default.ActivityToolboxLayout = _savedActivityToolboxLayout ?? string.Empty;
        RPA.Properties.Settings.Default.Save();
    }

    // --- Стражи исходников. ---

    [Test]
    public void Settings_DeclaresActivityToolboxLayout()
    {
        string settings = ReadSource(@"AiRpaStudio\Properties\Settings.settings");
        string appConfig = ReadSource(@"AiRpaStudio\App.config");
        string designer = ReadSource(@"AiRpaStudio\Properties\Settings.Designer.cs");

        Assert.Multiple(() =>
        {
            Assert.That(settings, Does.Contain("ActivityToolboxLayout"),
                "Settings.settings обязан объявлять настройку ActivityToolboxLayout");
            Assert.That(appConfig, Does.Contain("ActivityToolboxLayout"),
                "App.config обязан объявлять настройку ActivityToolboxLayout");
            Assert.That(designer, Does.Contain("public string ActivityToolboxLayout"),
                "Settings.Designer.cs обязан содержать свойство ActivityToolboxLayout");
        });
    }

    [Test]
    public void ToolboxPanel_PersistsCategoryStateToSettings()
    {
        string code = ReadSource(@"AiRpaStudio\Controls\ToolboxPanel.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ActivityToolboxLayout"),
                "панель обязана читать/писать настройку ActivityToolboxLayout");
            Assert.That(code, Does.Contain("Settings.Default.Save()"),
                "изменение состояния категорий сохраняется через Properties.Settings.Save()");
            Assert.That(code, Does.Contain("TreeViewItem.ExpandedEvent"),
                "разворачивание категории должно отслеживаться");
            Assert.That(code, Does.Contain("TreeViewItem.CollapsedEvent"),
                "сворачивание категории должно отслеживаться");
            Assert.That(code, Does.Contain("RestoreCategoryStates"),
                "состояние категорий обязано восстанавливаться");
        });
    }

    // --- Поведение (STA + настоящий ToolboxControl). ---

    [Test]
    public void CategoryExpandCollapse_PersistsToSettings()
    {
        DesignerTestHost.Shared.Invoke(CategoryExpandCollapse_PersistsToSettings_OnSta);
    }

    private void CategoryExpandCollapse_PersistsToSettings_OnSta()
    {
        RPA.Properties.Settings.Default.ActivityToolboxLayout = string.Empty;
        RPA.Properties.Settings.Default.Save();

        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            Assert.That(category, Is.Not.Null, "категория тулбокса не материализована");

            category.IsExpanded = false;
            Pump(window);

            List<string> collapsed = ReadExpandedFromSettings();
            Assert.That(collapsed, Does.Not.Contain("Flow"),
                "свёрнутая категория не должна оставаться в списке развёрнутых");

            category.IsExpanded = true;
            Pump(window);

            List<string> expanded = ReadExpandedFromSettings();
            Assert.That(expanded, Does.Contain("Flow"),
                "развёрнутая категория должна попасть в список развёрнутых");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Restore_AppliesSavedCollapsedState()
    {
        DesignerTestHost.Shared.Invoke(Restore_AppliesSavedCollapsedState_OnSta);
    }

    private void Restore_AppliesSavedCollapsedState_OnSta()
    {
        RPA.Properties.Settings.Default.ActivityToolboxLayout =
            Newtonsoft.Json.JsonConvert.SerializeObject(new List<string>());
        RPA.Properties.Settings.Default.Save();

        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            Assert.That(category, Is.Not.Null, "категория тулбокса не материализована");
            Assert.That(category.IsExpanded, Is.False,
                "сохранённое состояние «все свёрнуты» должно восстановиться");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Restore_AppliesMixedSavedState()
    {
        DesignerTestHost.Shared.Invoke(Restore_AppliesMixedSavedState_OnSta);
    }

    private void Restore_AppliesMixedSavedState_OnSta()
    {
        RPA.Properties.Settings.Default.ActivityToolboxLayout =
            Newtonsoft.Json.JsonConvert.SerializeObject(new List<string> { "Flow" });
        RPA.Properties.Settings.Default.Save();

        (ToolboxControl toolbox, Window window) = HostThemedToolbox(twoCategories: true);

        try
        {
            TreeViewItem flow = CategoryByName(toolbox, "Flow");
            TreeViewItem os = CategoryByName(toolbox, "OS");

            Assert.That(flow, Is.Not.Null, "категория Flow не материализована");
            Assert.That(os, Is.Not.Null, "категория OS не материализована");
            Assert.That(flow.IsExpanded, Is.True,
                "сохранённая развёрнутая категория должна остаться развёрнутой");
            Assert.That(os.IsExpanded, Is.False,
                "не сохранённая категория должна остаться свёрнутой");
        }
        finally
        {
            window.Close();
        }
    }

    // --- Инфраструктура. ---

    private static (ToolboxControl Toolbox, Window Window) HostThemedToolbox(bool twoCategories = false)
    {
        ToolboxControl toolbox = new ToolboxControl();

        ToolboxCategory flow = new ToolboxCategory("Flow")
        {
            new ToolboxItemWrapper(typeof(Sequence), null, "Sequence")
        };
        toolbox.Categories.Add(flow);

        if (twoCategories)
        {
            ToolboxCategory os = new ToolboxCategory("OS")
            {
                new ToolboxItemWrapper(typeof(WriteLine), null, "WriteLine")
            };
            toolbox.Categories.Add(os);
        }

        ToolboxPanel panel = new ToolboxPanel();
        panel.SetToolboxControl(toolbox);

        Window window = new Window
        {
            Width = 300,
            Height = 400,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = panel
        };

        window.Show();
        Pump(window);

        return (toolbox, window);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        window.UpdateLayout();
    }

    private static TreeViewItem FirstCategory(ToolboxControl toolbox)
    {
        TreeView tree = XamlHelper.FindVisualChildren<TreeView>(toolbox).FirstOrDefault();
        Assert.That(tree, Is.Not.Null, "в ToolboxControl нет PART_Tools");
        return FirstContainer(tree);
    }

    private static TreeViewItem CategoryByName(ToolboxControl toolbox, string name)
    {
        TreeView tree = XamlHelper.FindVisualChildren<TreeView>(toolbox).FirstOrDefault();
        Assert.That(tree, Is.Not.Null, "в ToolboxControl нет PART_Tools");
        tree.UpdateLayout();

        foreach (TreeViewItem item in XamlHelper.FindVisualChildren<TreeViewItem>(tree))
        {
            if (item.Header is ToolboxCategory category && category.CategoryName == name)
            {
                return item;
            }
        }

        return null;
    }

    private static TreeViewItem FirstContainer(ItemsControl parent)
    {
        parent?.UpdateLayout();
        return parent?.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
    }

    private static List<string> ReadExpandedFromSettings()
    {
        string json = RPA.Properties.Settings.Default.ActivityToolboxLayout;
        if (string.IsNullOrEmpty(json))
        {
            return new List<string>();
        }

        return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
