using System.Windows;
using System.Windows.Input;

namespace Cleanse10.Views
{
    public partial class BuildOptionsDialog : Window
    {
        public ViewModels.BuildOptionsViewModel ViewModel { get; }

        public BuildOptionsDialog(ViewModels.BuildOptionsViewModel vm)
        {
            InitializeComponent();
            ViewModel   = vm;
            DataContext = vm;

            // RequestClose fires on the UI thread (commands are synchronous)
            vm.RequestClose += confirmed =>
            {
                DialogResult = confirmed;
                Close();
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                return;

            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
