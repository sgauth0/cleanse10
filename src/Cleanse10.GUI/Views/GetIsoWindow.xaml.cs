using System.Windows;
using System.Windows.Input;

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
