using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Cleanse10.ViewModels;

namespace Cleanse10.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _viewModel.Activities.CollectionChanged += Activities_CollectionChanged;
        }

        private void Activities_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_viewModel.Activities.Count == 0)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                ActivityList.ScrollIntoView(_viewModel.Activities[^1]);
            });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
