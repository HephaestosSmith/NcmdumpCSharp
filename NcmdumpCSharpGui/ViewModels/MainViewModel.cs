using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NcmdumpCSharpGui.Helpers;
using NcmdumpCSharpGui.Models;
using NcmdumpCSharpGui.Services;

namespace NcmdumpCSharpGui.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DecryptionService _service = new();
    private CancellationTokenSource? _cts;

    // ── 資料夾路徑 ──────────────────────────────────────────────
    private string _sourceFolder = string.Empty;
    private string _outputFolder = string.Empty;

    public string SourceFolder
    {
        get => _sourceFolder;
        set
        {
            SetField(ref _sourceFolder, value);
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            SetField(ref _outputFolder, value);
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        }
    }

    // ── 選項 ────────────────────────────────────────────────────
    private bool _translateToTraditional = true;
    private bool _copyNonNcmFiles = false;
    private int  _maxParallelism  = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>
    /// 滑桿上限 = 物理核心數（估算值：邏輯處理器數 ÷ 2）。
    /// 5700G（8核16緒）→ 16÷2 = 8；無超執行緒 CPU 請手動調低。
    /// </summary>
    public int MaxParallelismMax => Math.Max(1, Environment.ProcessorCount / 2);

    public bool TranslateToTraditional
    {
        get => _translateToTraditional;
        set => SetField(ref _translateToTraditional, value);
    }

    public bool CopyNonNcmFiles
    {
        get => _copyNonNcmFiles;
        set => SetField(ref _copyNonNcmFiles, value);
    }

    public int MaxParallelism
    {
        get => _maxParallelism;
        set => SetField(ref _maxParallelism, value);
    }

    // ── 狀態 / 進度 ─────────────────────────────────────────────
    private bool   _isProcessing  = false;
    private int    _processedCount = 0;
    private int    _totalCount     = 0;
    private string _currentFile    = string.Empty;

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            SetField(ref _isProcessing, value);
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
        }
    }

    public int ProcessedCount
    {
        get => _processedCount;
        set => SetField(ref _processedCount, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            SetField(ref _totalCount, value);
            OnPropertyChanged(nameof(ProgressMaximum));
        }
    }

    /// <summary>防止 ProgressBar Maximum=0 造成顯示異常。</summary>
    public int ProgressMaximum => Math.Max(1, TotalCount);

    public string CurrentFile
    {
        get => _currentFile;
        set => SetField(ref _currentFile, value);
    }

    // ── 重複掃描子 ViewModel ─────────────────────────────────────
    public DuplicateScanViewModel DupVm { get; } = new();

    // ── 記錄 ────────────────────────────────────────────────────
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    // ── 命令 ────────────────────────────────────────────────────
    public ICommand StartCommand       { get; }
    public ICommand CancelCommand      { get; }
    public ICommand BrowseSourceCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand ClearLogCommand    { get; }

    public MainViewModel()
    {
        StartCommand = new RelayCommand(
            StartProcessing,
            () => !IsProcessing
               && !string.IsNullOrWhiteSpace(SourceFolder)
               && !string.IsNullOrWhiteSpace(OutputFolder));

        CancelCommand = new RelayCommand(
            CancelProcessing,
            () => IsProcessing);

        BrowseSourceCommand = new RelayCommand(BrowseSource);
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        ClearLogCommand     = new RelayCommand(() => LogEntries.Clear());
    }

    // ── 瀏覽按鈕 ───────────────────────────────────────────────
    private void BrowseSource()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = "選擇來源資料夾（包含 NCM 檔案）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrEmpty(SourceFolder) && Directory.Exists(SourceFolder))
            dialog.InitialDirectory = SourceFolder;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SourceFolder = dialog.SelectedPath;
    }

    private void BrowseOutput()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description            = "選擇輸出資料夾",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = true
        };

        if (!string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder))
            dialog.InitialDirectory = OutputFolder;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OutputFolder = dialog.SelectedPath;
    }

    // ── 開始 / 取消 ─────────────────────────────────────────────
    private async void StartProcessing()
    {
        IsProcessing   = true;
        ProcessedCount = 0;
        TotalCount     = 0;
        CurrentFile    = string.Empty;
        LogEntries.Clear();

        _cts = new CancellationTokenSource();

        var opts = new ProcessOptions
        {
            SourceFolder           = SourceFolder,
            OutputFolder           = OutputFolder,
            TranslateToTraditional = TranslateToTraditional,
            CopyNonNcmFiles        = CopyNonNcmFiles,
            MaxDegreeOfParallelism = MaxParallelism
        };

        var progressReporter = new Progress<ProgressReport>(r =>
        {
            TotalCount     = r.Total;
            ProcessedCount = r.Processed;
            CurrentFile    = r.CurrentFile;
        });

        var logReporter = new Progress<LogEntry>(entry =>
        {
            LogEntries.Add(entry);
        });

        try
        {
            var (total, succeeded, failed) = await _service.ProcessAsync(
                opts, progressReporter, logReporter, _cts.Token);

            LogEntries.Add(new LogEntry
            {
                Message = $"━━  全部完成！共 {total} 個，成功 {succeeded} 個，失敗 {failed} 個  ━━",
                Level   = failed == 0 ? LogLevel.成功 : LogLevel.警告
            });

            CurrentFile = string.Empty;
        }
        catch (OperationCanceledException)
        {
            LogEntries.Add(new LogEntry
            {
                Message = "━━  操作已由使用者取消  ━━",
                Level   = LogLevel.警告
            });
            CurrentFile = string.Empty;
        }
        catch (Exception ex)
        {
            LogEntries.Add(new LogEntry
            {
                Message = $"嚴重錯誤：{ex.Message}",
                Level   = LogLevel.錯誤
            });
        }
        finally
        {
            IsProcessing = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void CancelProcessing() => _cts?.Cancel();

    // ── INotifyPropertyChanged ───────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
