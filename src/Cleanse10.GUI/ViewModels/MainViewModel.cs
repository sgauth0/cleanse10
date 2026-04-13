using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private int    _wimIndex  = 1;

        private Preset10? _selectedPreset;
        private bool      _isBusy;
        private double    _progress;
        private string    _statusText = "Select an image file and choose a preset to begin.";
        private string    _log        = string.Empty;

        private List<WimImageInfo> _availableEditions = [];
        private WimImageInfo? _selectedEdition;

        // ──────────────────────────────────────────────────────────────────────
        // Public properties
        // ──────────────────────────────────────────────────────────────────────

        public string WimPath
        {
            get => _wimPath;
            set
            {
                SetField(ref _wimPath, value);
                RaiseCanExecute();
                _ = LoadEditionsAsync(value);
            }
        }

        public string MountPath
        {
            get => _mountPath;
            private set { SetField(ref _mountPath, value); RaiseCanExecute(); }
        }

        public int WimIndex
        {
            get => _wimIndex;
            set { SetField(ref _wimIndex, value); }
        }

        public List<WimImageInfo> AvailableEditions
        {
            get => _availableEditions;
            private set => SetField(ref _availableEditions, value);
        }

        public WimImageInfo? SelectedEdition
        {
            get => _selectedEdition;
            set
            {
                SetField(ref _selectedEdition, value);
                if (value != null)
                    WimIndex = value.Index;
            }
        }

        public bool HasMultipleEditions => _availableEditions.Count > 1;

        public Preset10? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                SetField(ref _selectedPreset, value);
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
        public List<PresetCardViewModel> Presets { get; }

        // ──────────────────────────────────────────────────────────────────────
        // Commands
        // ──────────────────────────────────────────────────────────────────────

        public ICommand BrowseImageCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectPresetCommand { get; }
        public ICommand GetIsoCommand { get; }
        public ICommand OpenSettingsCommand { get; }

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
            _settings = AppSettings.Load();
            _wimPath = _settings.LastWimPath;
            _mountPath = ResolveMountRoot(_settings.TempMountRoot);

            Presets = [];
            foreach (var def in PresetCatalog.All)
                Presets.Add(new PresetCardViewModel(def));

            BrowseImageCommand = new RelayCommand(BrowseImage);
            SelectPresetCommand = new RelayCommand<PresetCardViewModel>(card => SelectedPreset = card.Definition.Preset);
            GetIsoCommand = new RelayCommand(OpenGetIsoWindow);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RunCommand = new RelayCommand(async () => await RunAsync(), CanRun);
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

        private void BrowseImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a Windows 10 image file",
                Filter = "Image files (ISO, WIM, ESD)|*.iso;*.wim;*.esd|All files|*.*",
            };
            if (dlg.ShowDialog() != true)
                return;

            WimPath = dlg.FileName;
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

        private async Task LoadEditionsAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableEditions = [];
                    SelectedEdition = null;
                    OnPropertyChanged(nameof(HasMultipleEditions));
                });
                return;
            }

            if (string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase))
            {
                await LoadIsoEditionsAsync(path);
                return;
            }

            try
            {
                var wim = new WimManager();
                var images = await wim.GetInfoAsync(path);
                var list = images.ToList();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableEditions = list;
                    SelectedEdition = list.Count > 0 ? list[0] : null;
                    OnPropertyChanged(nameof(HasMultipleEditions));
                    if (list.Count > 1)
                        StatusText = $"Found {list.Count} editions - select one before building.";
                });
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableEditions = [];
                    SelectedEdition = null;
                    OnPropertyChanged(nameof(HasMultipleEditions));
                });
            }
        }

        private async Task LoadIsoEditionsAsync(string isoPath)
        {
            string? tempWim = null;

            IProgress<string> reporter = new Progress<string>(msg =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _logBuilder.Insert(0, msg + Environment.NewLine);
                    Log = _logBuilder.ToString();
                });
            });

            try
            {
                StatusText = "Inspecting ISO...";

                string installImageRelPath = await Task.Run(() =>
                    IsoReader.FindInstallImage(isoPath));

                tempWim = Path.Combine(Path.GetTempPath(),
                    $"cleanse10_probe_{Guid.NewGuid():N}{Path.GetExtension(installImageRelPath)}");

                await IsoReader.ExtractFileAsync(isoPath, installImageRelPath, tempWim,
                    reporter, byteProgress: null, CancellationToken.None);

                var wim = new WimManager();
                var images = await wim.GetInfoAsync(tempWim);
                var list = images.ToList();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableEditions = list;
                    SelectedEdition = list.Count > 0 ? list[0] : null;
                    OnPropertyChanged(nameof(HasMultipleEditions));
                    StatusText = list.Count > 1
                        ? $"Found {list.Count} editions in ISO - select one before building."
                        : "ISO ready.";
                });
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableEditions = [];
                    SelectedEdition = null;
                    OnPropertyChanged(nameof(HasMultipleEditions));
                    StatusText = "Could not read editions from ISO.";
                });
            }
            finally
            {
                if (tempWim != null)
                    try { File.Delete(tempWim); } catch { /* best effort */ }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Run
        // ──────────────────────────────────────────────────────────────────────

        private bool CanRun() =>
            !IsBusy &&
            !string.IsNullOrWhiteSpace(WimPath) && File.Exists(WimPath) &&
            !string.IsNullOrWhiteSpace(MountPath) &&
            SelectedPreset.HasValue;

        private async Task RunAsync()
        {
            var presetDef = PresetCatalog.Get(SelectedPreset!.Value);
            var dialogVm = new BuildOptionsViewModel(presetDef, _settings.LastOutputPath);
            var dialog = new Views.BuildOptionsDialog(dialogVm)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };

            if (dialog.ShowDialog() != true)
                return;

            var options = dialogVm.Result!;

            IsBusy = true;
            Progress = 0;
            Activities.Clear();
            _logBuilder.Clear();
            Log = string.Empty;
            _activeActivity = null;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string selectedImagePath = WimPath;
            string workingWimPath = WimPath;
            string? extractedWimPath = null;
            string? exportedWimDir = null;
            string? stagedIsoRoot = null;
            string mountPath = CreateMountSessionPath();
            _activeMountSessionPath = mountPath;

            IProgress<string> reporter = new Progress<string>(msg =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _logBuilder.Insert(0, msg + Environment.NewLine);
                    Log = _logBuilder.ToString();
                    HandleActivityMessage(msg);
                });
            });

            try
            {
                string? oscdimgPath = null;
                if (!string.IsNullOrWhiteSpace(options.OutputIso))
                {
                    StatusText = "Checking for oscdimg.exe...";
                    oscdimgPath = await IsoBuilder.EnsureOscdimgAsync(reporter, ct);
                }

                if (string.Equals(Path.GetExtension(selectedImagePath), ".iso", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(options.OutputIso))
                        throw new InvalidOperationException("Output ISO is required when the input is an ISO.");

                    StatusText = "Extracting install image from ISO...";
                    string installRelPath = await Task.Run(() =>
                        IsoReader.FindInstallImage(selectedImagePath));

                    exportedWimDir = Path.Combine(Path.GetTempPath(), $"cleanse10_iso_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(exportedWimDir);
                    extractedWimPath = Path.Combine(exportedWimDir, Path.GetFileName(installRelPath));

                    var byteProgress = new Progress<(long copied, long total)>(p =>
                    {
                        if (p.total > 0)
                            Progress = 5.0 * p.copied / p.total;
                    });

                    await IsoReader.ExtractFileAsync(
                        selectedImagePath, installRelPath, extractedWimPath,
                        reporter, byteProgress, ct);
                    Progress = 5;

                    StatusText = "Exporting selected edition...";
                    string exportedWimPath = Path.Combine(exportedWimDir, "exported.wim");
                    var exportWim = new WimManager();
                    workingWimPath = await exportWim.ExportImageToWimAsync(
                        extractedWimPath, WimIndex, exportedWimPath, reporter, ct);
                    Progress = 10;

                    try { File.Delete(extractedWimPath); } catch { /* best effort */ }
                    extractedWimPath = null;
                }

                string ext = Path.GetExtension(workingWimPath).ToLowerInvariant();
                bool isFromIso = string.Equals(Path.GetExtension(selectedImagePath), ".iso", StringComparison.OrdinalIgnoreCase);
                int mountIndex = (isFromIso || ext == ".esd") ? 1 : WimIndex;
                int effectiveWimIndex = mountIndex;

                _settings.LastWimPath = selectedImagePath;
                _settings.LastMountPath = mountPath;
                _settings.LastOutputPath = options.OutputIso ?? string.Empty;
                _settings.TempMountRoot = MountPath;
                _settings.Save();

                StatusText = "Preparing build workspace...";
                AddActivity($"Using temporary mount folder: {mountPath}");

                StatusText = "Mounting WIM...";
                var wim = new WimManager();
                await wim.MountAsync(workingWimPath, mountIndex, mountPath, reporter, ct);
                Progress = 15;

                StatusText = $"Running preset {SelectedPreset!.Value}...";

                var needsUnattend = options.AfkInstall || !string.IsNullOrWhiteSpace(options.Hostname);
                var unattendedCfg = needsUnattend
                    ? new Cleanse10.Core.Unattended.UnattendedConfig
                    {
                        ComputerName = string.IsNullOrWhiteSpace(options.Hostname) ? "*" : options.Hostname,
                        SkipOOBE = options.AfkInstall,
                        AcceptEula = options.AfkInstall,
                        HideEulaPage = options.AfkInstall,
                        HideWirelessPage = options.AfkInstall,
                        AdminUsername = options.AfkInstall ? options.AdminUsername : null,
                        AdminPassword = options.AfkInstall ? options.AdminPassword : null,
                        WimIndex = effectiveWimIndex,
                    }
                    : null;

                var runner = new PresetRunner10(mountPath, SelectedPreset!.Value)
                {
                    UnattendedConfig = unattendedCfg,
                    DriverFolder = string.IsNullOrWhiteSpace(options.DriverFolder) ? null : options.DriverFolder,
                    UpdateFolder = string.IsNullOrWhiteSpace(options.UpdateFolder) ? null : options.UpdateFolder,
                };

                await runner.RunAsync(reporter, ct);
                Progress = 80;

                StatusText = "Saving and unmounting...";
                await wim.UnmountAsync(mountPath, commit: true, reporter, ct);
                Progress = 90;

                reporter.Report($"[Cleanse10] ISO build check: OutputIso={options.OutputIso ?? "(null)"}, isFromIso={isFromIso}, selectedImagePath={selectedImagePath}");
                reporter.Report($"[Cleanse10] ISO build check: workingWimPath={workingWimPath}, workingWimExists={File.Exists(workingWimPath)}");

                if (!string.IsNullOrWhiteSpace(options.OutputIso))
                {
                    string? isoSource = null;

                    if (isFromIso)
                    {
                        StatusText = "Staging ISO contents...";
                        stagedIsoRoot = Path.Combine(Path.GetTempPath(), $"cleanse10_iso_stage_{Guid.NewGuid():N}");
                        reporter.Report($"[Cleanse10] Staging ISO contents from {selectedImagePath} -> {stagedIsoRoot}");
                        await IsoReader.StageContentsAsync(selectedImagePath, stagedIsoRoot, reporter, ct);

                        string stagedSources = Path.Combine(stagedIsoRoot, "sources");
                        Directory.CreateDirectory(stagedSources);
                        string stagedWimDest = Path.Combine(stagedSources, "install.wim");
                        reporter.Report($"[Cleanse10] Copying serviced WIM ({new FileInfo(workingWimPath).Length / 1048576} MB) -> {stagedWimDest}");
                        File.Copy(workingWimPath, stagedWimDest, overwrite: true);
                        isoSource = stagedIsoRoot;
                    }
                    else
                    {
                        string? wimDir = Path.GetDirectoryName(workingWimPath);
                        string? isoRoot = wimDir != null ? Path.GetDirectoryName(wimDir) : null;

                        reporter.Report($"[Cleanse10] WIM-only path: wimDir={wimDir}, isoRoot={isoRoot}");

                        if (isoRoot != null
                            && Directory.Exists(Path.Combine(isoRoot, "boot"))
                            && Directory.Exists(Path.Combine(isoRoot, "sources")))
                        {
                            isoSource = isoRoot;
                        }
                        else
                        {
                            reporter.Report("[WARN] Skipping ISO build: the WIM is not inside a valid Windows ISO directory tree (expected boot\\ and sources\\ alongside the WIM).");
                            reporter.Report("[WARN] To create an output ISO, use an ISO file as input instead.");
                        }
                    }

                    reporter.Report($"[Cleanse10] isoSource={isoSource ?? "(null)"}, oscdimgPath={oscdimgPath ?? "(null)"}");

                    if (isoSource != null)
                    {
                        if (unattendedCfg != null && options.AfkInstall)
                        {
                            reporter.Report("[Cleanse10] Writing autounattend.xml to ISO root...");
                            Cleanse10.Core.Unattended.UnattendedGenerator.WriteToIsoRoot(unattendedCfg, isoSource);
                            reporter.Report($"[Cleanse10] autounattend.xml written to: {isoSource}");
                        }

                        StatusText = "Building ISO...";
                        reporter.Report($"[Cleanse10] Building ISO: source={isoSource}, output={options.OutputIso}");
                        var builder = new IsoBuilder(oscdimgPath!);
                        await builder.BuildAsync(isoSource, options.OutputIso, reporter, ct);

                        if (File.Exists(options.OutputIso))
                        {
                            long sizeMb = new FileInfo(options.OutputIso).Length / 1048576;
                            reporter.Report($"[Cleanse10] ISO created successfully: {options.OutputIso} ({sizeMb} MB)");
                        }
                        else
                        {
                            reporter.Report($"[ERR] ISO build completed but output file not found at: {options.OutputIso}");
                        }
                    }
                    else
                    {
                        reporter.Report("[WARN] ISO build skipped - no valid ISO source directory.");
                    }
                }
                else
                {
                    reporter.Report("[Cleanse10] No output ISO path specified - skipping ISO build.");
                }

                Progress = 100;
                StatusText = "Done!";
                AddActivity("Your image is ready.");
                CompleteActiveActivity();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Cancelled.";
                AddActivity("Operation cancelled.", isError: true);
                try
                {
                    var wim = new WimManager();
                    await wim.UnmountAsync(mountPath, commit: false, reporter, CancellationToken.None);
                }
                catch { /* best effort */ }
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                reporter.Report($"[ERROR] {ex}");
                try
                {
                    var wim = new WimManager();
                    await wim.UnmountAsync(mountPath, commit: false, reporter, CancellationToken.None);
                }
                catch { /* best effort */ }
                System.Windows.MessageBox.Show(ex.Message, "Cleanse10 - Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                CompleteActiveActivity();
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;

                if (extractedWimPath != null)
                    try { File.Delete(extractedWimPath); } catch { /* best effort */ }

                if (exportedWimDir != null)
                    try { Directory.Delete(exportedWimDir, recursive: true); } catch { /* best effort */ }

                if (stagedIsoRoot != null)
                    try { Directory.Delete(stagedIsoRoot, recursive: true); } catch { /* best effort */ }

                CleanupMountSession();
                _activeMountSessionPath = null;
            }
        }

        private void Cancel() => _cts?.Cancel();

        private void RaiseCanExecute()
        {
            (RunCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

            AddActivity(message, isError: message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase));
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

    internal class RelayCommand<T> : System.Windows.Input.ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => System.Windows.Input.CommandManager.RequerySuggested += value;
            remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? p) => _canExecute?.Invoke((T)p!) ?? true;
        public void Execute(object? p) => _execute((T)p!);
    }
}
