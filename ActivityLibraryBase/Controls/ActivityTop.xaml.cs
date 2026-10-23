using System.Windows;
using System.Windows.Controls;

namespace ActivityLibraryBase.Controls
{
    public partial class ActivityTop : UserControl
    {
        public static readonly DependencyProperty TitleTextProperty =
            DependencyProperty.Register("TitleText", typeof(string), typeof(ActivityTop), new PropertyMetadata(string.Empty));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        public static readonly DependencyProperty IsEnabledActivityProperty =
            DependencyProperty.Register("IsEnabledActivity", typeof(bool), typeof(ActivityTop));

        public bool IsEnabledActivity
        {
            get => isEnabledCheckBox.IsChecked ?? false;
            set => SetValue(IsEnabledActivityProperty, value);
        }

        public ActivityTop()
        {
            InitializeComponent();
        }

        private void IsEnabledCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            IsEnabledActivity = true;
        }

        private void IsEnabledCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            IsEnabledActivity = false;
        }
    }
}
