using System.Windows;
using System.Windows.Input;

namespace Cleanse10.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(ViewModels.SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

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
