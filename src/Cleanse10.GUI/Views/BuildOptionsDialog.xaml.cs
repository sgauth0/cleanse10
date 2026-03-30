using System.Windows;

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
    }
}
