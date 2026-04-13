using System;
using System.IO;
using System.Windows.Input;

namespace Cleanse10.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly string _defaultMountRoot;
    private string _mountRoot;

    public SettingsViewModel(string currentMountRoot, string defaultMountRoot)
    {
        _mountRoot = currentMountRoot;
        _defaultMountRoot = defaultMountRoot;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        BrowseMountRootCommand = new RelayCommand(BrowseMountRoot);
        UseDefaultCommand = new RelayCommand(() => MountRoot = _defaultMountRoot);
    }

    public string MountRoot
    {
        get => _mountRoot;
        set => SetField(ref _mountRoot, value);
    }

    public string DefaultMountRoot => _defaultMountRoot;

    public string? ResultMountRoot { get; private set; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseMountRootCommand { get; }
    public ICommand UseDefaultCommand { get; }

    public event Action<bool>? RequestClose;

    private void BrowseMountRoot()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a workspace for temporary mount folders",
        };

        if (dlg.ShowDialog() == true)
            MountRoot = dlg.FolderName;
    }

    private void Save()
    {
        string mountRoot = string.IsNullOrWhiteSpace(MountRoot) ? _defaultMountRoot : MountRoot.Trim();
        Directory.CreateDirectory(mountRoot);
        ResultMountRoot = mountRoot;
        RequestClose?.Invoke(true);
    }
}
