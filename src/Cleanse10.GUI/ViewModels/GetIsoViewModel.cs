using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Cleanse10.Core.Media;

namespace Cleanse10.ViewModels
{
    public class GetIsoViewModel : ViewModelBase
    {
        // ── State ──────────────────────────────────────────────────────────────

        private string   _outputFolder;
        private bool     _isBusy;
        private double   _progressPercent;
        private string   _statusText          = "Choose a save folder, then click Download.";
        private string   _speedText           = string.Empty;
        private bool     _isDownloadComplete;
        private string   _downloadedIsoPath   = string.Empty;
        private long     _downloadedBytes;
        private long     _totalBytes;
        private DateTime _opStart;

        private CancellationTokenSource? _cts;

        // ── Constructor ────────────────────────────────────────────────────────

        public GetIsoViewModel()
        {
            _outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            DownloadCommand     = new RelayCommand(async () => await DownloadAsync(), CanDownload);
            CancelCommand       = new RelayCommand(Cancel, () => IsBusy);
            ExtractWimCommand   = new RelayCommand(async () => await ExtractWimAsync(),
                                      () => IsDownloadComplete && !IsBusy);
        }

        // ── Public properties ──────────────────────────────────────────────────

        public string OutputFolder
        {
            get => _outputFolder;
            set { SetField(ref _outputFolder, value); RaiseCanExecute(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { SetField(ref _isBusy, value); RaiseCanExecute(); }
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetField(ref _progressPercent, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public string SpeedText
        {
            get => _speedText;
            private set => SetField(ref _speedText, value);
        }

        public bool IsDownloadComplete
        {
            get => _isDownloadComplete;
            private set { SetField(ref _isDownloadComplete, value); RaiseCanExecute(); }
        }

        public string DownloadedIsoPath
        {
            get => _downloadedIsoPath;
            private set => SetField(ref _downloadedIsoPath, value);
        }

        /// <summary>Populated when the ISO is ready to use; read by the caller after the window closes.</summary>
        public string? ResultWimPath { get; private set; }

        // ── Commands ───────────────────────────────────────────────────────────

        public ICommand BrowseOutputCommand { get; }
        public ICommand DownloadCommand     { get; }
        public ICommand CancelCommand       { get; }
        public ICommand ExtractWimCommand   { get; }

        /// <summary>Raised on the task thread when the window should close.</summary>
        public event Action? RequestClose;

        // ── Browse ─────────────────────────────────────────────────────────────

        private void BrowseOutput()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose folder to save Windows 10 ISO",
            };
            if (dlg.ShowDialog() == true)
                OutputFolder = dlg.FolderName;
        }

        // ── Download ───────────────────────────────────────────────────────────

        private bool CanDownload() => !IsBusy && !string.IsNullOrWhiteSpace(OutputFolder);

        private async Task DownloadAsync()
        {
            IsBusy             = true;
            IsDownloadComplete = false;
            ProgressPercent    = 0;
            SpeedText          = string.Empty;
            _opStart           = DateTime.UtcNow;
            _cts               = new CancellationTokenSource();
            var ct             = _cts.Token;

            IProgress<string> log = new Progress<string>(msg => StatusText = msg);

            IProgress<(long downloaded, long total)> byteProgress =
                new Progress<(long downloaded, long total)>(t =>
                {
                    _downloadedBytes = t.downloaded;
                    _totalBytes      = t.total;
                    if (t.total > 0)
                        ProgressPercent = t.downloaded * 100.0 / t.total;
                    UpdateSpeedText(t.downloaded, t.total);
                });

            try
            {
                string url = await Win10DownloadService.GetDownloadUrlAsync(
                    Win10Languages.Default, log, ct);

                string isoPath  = Path.Combine(OutputFolder, "Win10_22H2_Pro_English.iso");
                StatusText = $"Downloading → {isoPath}";

                await Win10DownloadService.DownloadIsoAsync(url, isoPath, log, byteProgress, ct);

                DownloadedIsoPath  = isoPath;
                IsDownloadComplete = true;
                ProgressPercent    = 100;
                SpeedText          = FormatBytes(_totalBytes > 0 ? _totalBytes : _downloadedBytes)
                                     + " downloaded";
            }
            catch (OperationCanceledException)
            {
                StatusText      = "Download cancelled.";
                SpeedText       = string.Empty;
                ProgressPercent = 0;
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                SpeedText  = string.Empty;
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ── Use Downloaded ISO ─────────────────────────────────────────────────

        private async Task ExtractWimAsync()
        {
            IsBusy          = true;
            ProgressPercent = 0;
            SpeedText       = string.Empty;
            _opStart        = DateTime.UtcNow;
            _cts            = new CancellationTokenSource();
            var ct          = _cts.Token;

            try
            {
                await Task.Yield();
                ResultWimPath   = DownloadedIsoPath;
                ProgressPercent = 100;
                StatusText      = $"ISO ready → {DownloadedIsoPath}";
                RequestClose?.Invoke();
            }
            catch (OperationCanceledException)
            {
                StatusText      = "Selection cancelled.";
                SpeedText       = string.Empty;
                ProgressPercent = 0;
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                SpeedText  = string.Empty;
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Cancel() => _cts?.Cancel();

        private void RaiseCanExecute()
        {
            (DownloadCommand   as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand     as RelayCommand)?.RaiseCanExecuteChanged();
            (ExtractWimCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void UpdateSpeedText(long done, long total)
        {
            double elapsed = (DateTime.UtcNow - _opStart).TotalSeconds;
            if (elapsed < 0.5) return;

            double mbps = done / elapsed / 1_048_576.0;
            double eta  = total > 0 ? (total - done) / (done / elapsed) : -1;

            SpeedText = $"{FormatBytes(done)} / {FormatBytes(total)}" +
                        $"  •  {mbps:F1} MB/s" +
                        (eta > 0 ? $"  •  {FormatTime(eta)}" : "");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)              return "—";
            if (bytes < 1_048_576)      return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1_073_741_824L) return $"{bytes / 1_048_576.0:F1} MB";
            return $"{bytes / 1_073_741_824.0:F2} GB";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 60)   return $"{seconds:F0}s remaining";
            if (seconds < 3600) return $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s remaining";
            return $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60)}m remaining";
        }
    }
}
