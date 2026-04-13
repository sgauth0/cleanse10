using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Cleanse10.Core.Imaging;
using Cleanse10.Core.Media;
using Cleanse10.Core.Presets;
using Cleanse10.Core.Settings;

namespace Cleanse10.ViewModels
{
    /// <summary>Wraps a PresetDefinition with an IsSelected flag for the card UI.</summary>
    public class PresetCardViewModel : ViewModelBase
    {
        public PresetDefinition Definition { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool HasAdds => Definition.Adds.Length > 0;

        public PresetCardViewModel(PresetDefinition definition)
        {
            Definition = definition;
        }
    }

    public class MainViewModel : ViewModelBase
    {
        // ──────────────────────────────────────────────────────────────────────
        // State
        // ──────────────────────────────────────────────────────────────────────

        private string _wimPath   = string.Empty;
        private string _mountPath = string.Empty;
        private string _outputIso = string.Empty;
        private int    _wimIndex  = 1;

        private Preset10? _selectedPreset;
        private bool      _isBusy;
        private double    _progress;
        private string    _statusText = "Select a WIM and choose a preset to begin.";
        private string    _log        = string.Empty;

        // ──────────────────────────────────────────────────────────────────────
        // Public properties
        // ──────────────────────────────────────────────────────────────────────

        public string WimPath
        {
            get => _wimPath;
            set { SetField(ref _wimPath, value); RaiseCanExecute(); }
        }

        public string MountPath
        {
            get => _mountPath;
            private set { SetField(ref _mountPath, value); RaiseCanExecute(); }
        }

        public string OutputIso
        {
            get => _outputIso;
            set { SetField(ref _outputIso, value); }
        }

        public int WimIndex
        {
            get => _wimIndex;
            set { SetField(ref _wimIndex, value); }
        }

        public Preset10? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                SetField(ref _selectedPreset, value);
                // Sync IsSelected on each card
                foreach (var card in Presets)
                    card.IsSelected = card.Definition.Preset == value;
                RaiseCanExecute();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { SetField(ref _isBusy, value); RaiseCanExecute(); }
        }

        public double Progress
        {
            get => _progress;
            private set => SetField(ref _progress, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public string Log
        {
            get => _log;
            private set => SetField(ref _log, value);
        }

        public ObservableCollection<ActivityItemViewModel> Activities { get; } = [];

        // Preset card view models for the UI to bind to
        public List<PresetCardViewModel> Presets { get; }

        // ──────────────────────────────────────────────────────────────────────
        // Commands
        // ──────────────────────────────────────────────────────────────────────

        public ICommand BrowseWimCommand   { get; }
        public ICommand BrowseIsoCommand   { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunCommand         { get; }
        public ICommand CancelCommand      { get; }
        public ICommand SelectPresetCommand { get; }
        public ICommand GetIsoCommand      { get; }

        // ──────────────────────────────────────────────────────────────────────
        // Private
        // ──────────────────────────────────────────────────────────────────────

        private CancellationTokenSource? _cts;
        private readonly AppSettings _settings;
        private readonly StringBuilder _logBuilder = new();
        private ActivityItemViewModel? _activeActivity;
        private string? _activeMountSessionPath;

        // ──────────────────────────────────────────────────────────────────────
        // Constructor
        // ──────────────────────────────────────────────────────────────────────

        public MainViewModel()
        {
            _settings  = AppSettings.Load();
            _wimPath   = _settings.LastWimPath;
            _mountPath = ResolveMountRoot(_settings.TempMountRoot);
            _outputIso = _settings.LastOutputPath;

            // Build card VMs from catalog
            Presets = new List<PresetCardViewModel>();
            foreach (var def in PresetCatalog.All)
                Presets.Add(new PresetCardViewModel(def));

            BrowseWimCommand    = new RelayCommand(BrowseWim);
            BrowseIsoCommand    = new RelayCommand(BrowseIso);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            SelectPresetCommand = new RelayCommand<PresetCardViewModel>(card => SelectedPreset = card.Definition.Preset);
            GetIsoCommand = new RelayCommand(OpenGetIsoWindow);
            RunCommand    = new RelayCommand(async () => await RunAsync(), CanRun);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Browse helpers
        // ──────────────────────────────────────────────────────────────────────

        private void OpenGetIsoWindow()
        {
            var win = new Views.GetIsoWindow
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            win.ShowDialog();
            if (win.ViewModel.ResultWimPath != null)
                WimPath = win.ViewModel.ResultWimPath;
        }

        private void BrowseWim()        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Select Windows 10 WIM / ESD file",
                Filter = "WIM / ESD files|*.wim;*.esd|All files|*.*",
            };
            if (dlg.ShowDialog() == true) WimPath = dlg.FileName;
        }

        private void BrowseIso()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Select a Windows 10 ISO",
                Filter = "ISO images|*.iso|All files|*.*",
            };
            if (dlg.ShowDialog() == true)
                _ = ExtractIsoAsync(dlg.FileName);
        }

        private async Task ExtractIsoAsync(string isoPath)
        {
            IsBusy   = true;
            Progress = 0;
            _logBuilder.Clear();
            Log        = string.Empty;
            StatusText = "Preparing to extract install.wim from ISO...";

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            IProgress<string> reporter = new Progress<string>(msg =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _logBuilder.AppendLine(msg);
                    Log = _logBuilder.ToString();
                });
            });

            IProgress<(long copied, long total)> byteProgress =
                new Progress<(long copied, long total)>(t =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (t.total > 0)
                            Progress = t.copied * 100.0 / t.total;
                    });
                });

            try
            {
                string outputFolder = Path.GetDirectoryName(isoPath)!;
                StatusText = "Extracting install.wim from ISO...";
                string wimPath = await Win10DownloadService.ExtractInstallWimAsync(
                    isoPath, outputFolder, reporter, byteProgress, ct);

                WimPath    = wimPath;
                Progress   = 100;
                StatusText = "Extracted — WIM path loaded.";
                reporter.Report($"[Cleanse10] install.wim extracted to {wimPath}");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Extraction cancelled.";
                reporter.Report("[Cleanse10] ISO extraction cancelled by user.");
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                reporter.Report($"[ERROR] {ex}");
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OpenSettings()
        {
            var dialogVm = new SettingsViewModel(MountPath, GetDefaultMountRoot());
            var dialog = new Views.SettingsDialog(dialogVm)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialogVm.ResultMountRoot))
                return;

            MountPath = dialogVm.ResultMountRoot;
            _settings.TempMountRoot = MountPath;
            _settings.Save();
        }

        private void BrowseOutput()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title  = "Save rebuilt ISO as…",
                Filter = "ISO image|*.iso",
                FileName = "cleanse10.iso",
            };
            if (dlg.ShowDialog() == true) OutputIso = dlg.FileName;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Run
        // ──────────────────────────────────────────────────────────────────────

        private bool CanRun() =>
            !IsBusy &&
            !string.IsNullOrWhiteSpace(WimPath)    && File.Exists(WimPath) &&
            !string.IsNullOrWhiteSpace(MountPath) &&
            SelectedPreset.HasValue;

        private async Task RunAsync()
        {
            // ── Show pre-build options dialog ────────────────────────────────
            var presetDef = PresetCatalog.Get(SelectedPreset!.Value);
            var dialogVm  = new BuildOptionsViewModel(presetDef);
            var dialog    = new Views.BuildOptionsDialog(dialogVm)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };

            if (dialog.ShowDialog() != true)
                return;   // user cancelled

            var options = dialogVm.Result!;

            IsBusy   = true;
            Progress = 0;
            Activities.Clear();
            _logBuilder.Clear();
            Log = string.Empty;
            _activeActivity = null;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string mountPath = CreateMountSessionPath();
            _activeMountSessionPath = mountPath;

            IProgress<string> reporter = new Progress<string>(msg =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    HandleActivityMessage(msg);
                });
            });

            try
            {
                // Save settings
                _settings.LastWimPath    = WimPath;
                _settings.LastMountPath  = mountPath;
                _settings.LastOutputPath = OutputIso;
                _settings.TempMountRoot  = MountPath;
                _settings.Save();

                StatusText = "Preparing build workspace...";
                AddActivity($"Using temporary mount folder: {mountPath}");

                StatusText = "Mounting WIM…";
                var wim = new WimManager();
                await wim.MountAsync(WimPath, WimIndex, mountPath, reporter, ct);

                StatusText = $"Running preset {SelectedPreset!.Value}…";

                // Map build options → unattended config
                var needsUnattend = options.AfkInstall || !string.IsNullOrWhiteSpace(options.Hostname);
                var unattendedCfg = needsUnattend
                    ? new Cleanse10.Core.Unattended.UnattendedConfig
                    {
                        ComputerName     = string.IsNullOrWhiteSpace(options.Hostname) ? "*" : options.Hostname,
                        SkipOOBE         = options.AfkInstall,
                        AcceptEula       = options.AfkInstall,
                        HideEulaPage     = options.AfkInstall,
                        HideWirelessPage = options.AfkInstall,
                        AdminUsername    = options.AfkInstall ? options.AdminUsername : null,
                        AdminPassword    = options.AfkInstall ? options.AdminPassword : null,
                        WimIndex         = WimIndex,
                    }
                    : null;

                var runner = new PresetRunner10(mountPath, SelectedPreset!.Value)
                {
                    UnattendedConfig = unattendedCfg,
                    DriverFolder     = string.IsNullOrWhiteSpace(options.DriverFolder) ? null : options.DriverFolder,
                };

                await runner.RunAsync(reporter, ct);

                StatusText = "Saving and unmounting…";
                await wim.UnmountAsync(mountPath, commit: true, reporter, ct);

                if (!string.IsNullOrWhiteSpace(OutputIso))
                {
                    // The WIM lives at <isoRoot>\sources\install.wim — walk up two levels
                    // to reach the ISO root directory that contains boot\, efi\, sources\, etc.
                    string? wimDir    = Path.GetDirectoryName(WimPath);
                    string  isoSource = (wimDir != null ? Path.GetDirectoryName(wimDir) : null)
                                        ?? mountPath;

                    // Write autounattend.xml at the ISO root for a fully unattended install.
                    // This is separate from the Windows\Panther\unattend.xml already written
                    // inside the WIM by PresetRunner10: autounattend.xml handles the windowsPE
                    // pass (disk partitioning + image selection), while Panther\unattend.xml
                    // handles specialize + oobeSystem (hostname, account, OOBE skip).
                    if (unattendedCfg != null)
                    {
                        reporter.Report("[Cleanse10] Writing autounattend.xml to ISO root…");
                        Cleanse10.Core.Unattended.UnattendedGenerator.WriteToIsoRoot(unattendedCfg, isoSource);
                        reporter.Report($"[Cleanse10] autounattend.xml written to: {isoSource}");
                    }

                    StatusText = "Building ISO…";
                    var builder = new IsoBuilder();
                    await builder.BuildAsync(isoSource, OutputIso, reporter, ct);
                }

                StatusText = "Done!";
                AddActivity("Your image is ready.");
                CompleteActiveActivity();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Cancelled.";
                AddActivity("Operation cancelled.", isError: true);
                // Best-effort unmount without commit
                try
                {
                    var wim = new WimManager();
                    await wim.UnmountAsync(mountPath, commit: false, reporter, CancellationToken.None);
                }
                catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                reporter.Report($"[ERROR] {ex}");
                // Best-effort discard unmount — an exception mid-pipeline leaves the WIM
                // mounted, which causes exit code 13 on every subsequent run attempt.
                try
                {
                    var wim = new WimManager();
                    await wim.UnmountAsync(mountPath, commit: false, reporter, CancellationToken.None);
                }
                catch { /* best effort */ }
                System.Windows.MessageBox.Show(ex.Message, "Cleanse10 — Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                CompleteActiveActivity();
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
                CleanupMountSession();
                _activeMountSessionPath = null;
            }
        }

        private void Cancel() => _cts?.Cancel();

        private void RaiseCanExecute()
        {
            (RunCommand    as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private static string GetDefaultMountRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cleanse10",
                "Mounts");
        }

        private static string ResolveMountRoot(string? configuredMountRoot)
        {
            return string.IsNullOrWhiteSpace(configuredMountRoot)
                ? GetDefaultMountRoot()
                : configuredMountRoot.Trim();
        }

        private string CreateMountSessionPath()
        {
            Directory.CreateDirectory(MountPath);
            return Path.Combine(MountPath, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        }

        private void CleanupMountSession()
        {
            if (string.IsNullOrWhiteSpace(_activeMountSessionPath) || !Directory.Exists(_activeMountSessionPath))
                return;

            try
            {
                Directory.Delete(_activeMountSessionPath, recursive: true);
            }
            catch
            {
                // best effort
            }
        }

        private void HandleActivityMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            StatusText = message;

            if (!message.StartsWith("[", StringComparison.Ordinal))
                return;

            AddActivity(message, isError: message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) ||
                                         message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase));
        }

        private void AddActivity(string message, bool isError = false)
        {
            if (_activeActivity != null && string.Equals(_activeActivity.Message, message, StringComparison.Ordinal))
            {
                _activeActivity.IsError = isError;
                return;
            }

            CompleteActiveActivity();

            var item = new ActivityItemViewModel(message)
            {
                IsActive = true,
                IsError = isError,
            };

            Activities.Add(item);
            _activeActivity = item;
        }

        private void CompleteActiveActivity()
        {
            if (_activeActivity == null)
                return;

            _activeActivity.IsActive = false;
            _activeActivity.IsCompleted = true;
            _activeActivity = null;
        }
    }

    // ── Typed RelayCommand helper ─────────────────────────────────────────────

    internal class RelayCommand<T> : System.Windows.Input.ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => System.Windows.Input.CommandManager.RequerySuggested += value;
            remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? p) => _canExecute?.Invoke((T)p!) ?? true;
        public void Execute(object? p)    => _execute((T)p!);
    }
}
