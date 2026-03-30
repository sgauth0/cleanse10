using System.Windows;
using Cleanse10.ViewModels;

namespace Cleanse10.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}
