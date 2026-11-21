using System.Windows;
using System.Windows.Controls;
using RPA.Locals;
using RPA.Models;

namespace RPA.Controls
{
    /// <summary>
    /// Панель «Локальные переменные»: дерево переменных (Name/Value/Type) и область деталей
    /// выбранного узла (дерево для объектов/коллекций/словарей, DataGrid для DataTable/DataView).
    /// </summary>
    public partial class LocalsControl : UserControl
    {
        public LocalsControl()
        {
            InitializeComponent();
        }

        private void LocalsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is LocalsModel model)
            {
                model.SelectedNode = e.NewValue as LocalsNode;
            }
        }
    }
}
