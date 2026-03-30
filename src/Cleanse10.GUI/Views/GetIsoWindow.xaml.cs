using System.Windows;

namespace Cleanse10.Views
{
    public partial class GetIsoWindow : Window
    {
        public ViewModels.GetIsoViewModel ViewModel { get; }

        public GetIsoWindow()
        {
            InitializeComponent();
            ViewModel = new ViewModels.GetIsoViewModel();
            DataContext = ViewModel;

            // RequestClose fires on a task thread — marshal to UI thread
            ViewModel.RequestClose += () => Dispatcher.Invoke(Close);
        }
    }
}
