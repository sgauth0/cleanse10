using System.Windows.Input;
using Cleanse10.Core.Presets;

namespace Cleanse10.ViewModels
{
    /// <summary>
    /// View model for the pre-build options dialog.
    /// Exposes all user-configurable options and the confirm/cancel commands.
    /// </summary>
    public class BuildOptionsViewModel : ViewModelBase
    {
        // ──────────────────────────────────────────────────────────────────────
        // Preset context (display only)
        // ──────────────────────────────────────────────────────────────────────

        public string PresetName    { get; }
        public string PresetTagline { get; }
        public string PresetIcon    { get; }

        // ──────────────────────────────────────────────────────────────────────
        // Options
        // ──────────────────────────────────────────────────────────────────────

        private string _hostname = string.Empty;
        public string Hostname
        {
            get => _hostname;
            set => SetField(ref _hostname, value);
        }

        private bool _afkInstall;
        public bool AfkInstall
        {
            get => _afkInstall;
            set
            {
                SetField(ref _afkInstall, value);
                OnPropertyChanged(nameof(AdminSectionVisible));
            }
        }

        /// <summary>Controls visibility of the admin account section.</summary>
        public bool AdminSectionVisible => _afkInstall;

        private string _adminUsername = string.Empty;
        public string AdminUsername
        {
            get => _adminUsername;
            set => SetField(ref _adminUsername, value);
        }

        private string _adminPassword = string.Empty;
        public string AdminPassword
        {
            get => _adminPassword;
            set => SetField(ref _adminPassword, value);
        }

        private string _driverFolder = string.Empty;
        public string DriverFolder
        {
            get => _driverFolder;
            set => SetField(ref _driverFolder, value);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Result
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Set to the collected options when the user clicks Start Build. Null if cancelled.</summary>
        public BuildOptions? Result { get; private set; }

        // ──────────────────────────────────────────────────────────────────────
        // Events
        // ──────────────────────────────────────────────────────────────────────

        public event System.Action<bool>? RequestClose;

        // ──────────────────────────────────────────────────────────────────────
        // Commands
        // ──────────────────────────────────────────────────────────────────────

        public ICommand ConfirmCommand       { get; }
        public ICommand CancelCommand        { get; }
        public ICommand BrowseDriversCommand { get; }

        // ──────────────────────────────────────────────────────────────────────
        // Constructor
        // ──────────────────────────────────────────────────────────────────────

        public BuildOptionsViewModel(PresetDefinition preset)
        {
            PresetName    = preset.Name;
            PresetTagline = preset.Tagline;
            PresetIcon    = preset.Icon;

            ConfirmCommand       = new RelayCommand(Confirm);
            CancelCommand        = new RelayCommand(() => RequestClose?.Invoke(false));
            BrowseDriversCommand = new RelayCommand(BrowseDrivers);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Browse driver folder
        // ──────────────────────────────────────────────────────────────────────

        private void BrowseDrivers()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder containing .inf driver files",
            };
            if (dlg.ShowDialog() == true)
                DriverFolder = dlg.FolderName;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Confirm
        // ──────────────────────────────────────────────────────────────────────

        private void Confirm()
        {
            Result = new BuildOptions
            {
                Hostname      = string.IsNullOrWhiteSpace(Hostname) ? null : Hostname.Trim(),
                AfkInstall    = AfkInstall,
                AdminUsername = AfkInstall && !string.IsNullOrWhiteSpace(AdminUsername) ? AdminUsername.Trim() : null,
                AdminPassword = AfkInstall ? AdminPassword : null,
                DriverFolder  = string.IsNullOrWhiteSpace(DriverFolder) ? null : DriverFolder.Trim(),
            };
            RequestClose?.Invoke(true);
        }
    }
}
