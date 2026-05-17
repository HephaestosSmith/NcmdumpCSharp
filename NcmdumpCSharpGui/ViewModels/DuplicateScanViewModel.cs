using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ATL;
using NcmdumpCSharpGui.Helpers;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace NcmdumpCSharpGui.ViewModels;

// ── 單一重複檔案條目 ────────────────────────────────────────────
public class DuplicateFileItem : INotifyPropertyChanged
{
    public string FilePath    { get; init; } = string.Empty;
    public string FileName    => Path.GetFileName(FilePath);
    public string DisplaySize { get; init; } = string.Empty;
    public string FolderPath  => Path.GetDirectoryName(FilePath) ?? string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke();
        }
    }

    internal Action? SelectionChanged { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ── 一組重複檔案 ────────────────────────────────────────────────
public class DuplicateGroupViewModel
{
    public string GroupLabel { get; init; } = string.Empty;
    public string CountLabel => $"{Items.Count} 個檔案";

    public ObservableCollection<DuplicateFileItem> Items { get; } = new();

    public ICommand SelectAllCommand  { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand KeepFirstCommand  { get; }

    public DuplicateGroupViewModel()
    {
        SelectAllCommand  = new RelayCommand(() => { foreach (var i in Items) i.IsSelected = true; });
        SelectNoneCommand = new RelayCommand(() => { foreach (var i in Items) i.IsSelected = false; });
        KeepFirstCommand  = new RelayCommand(() =>
        {
            for (int i = 0; i < Items.Count; i++)
                Items[i].IsSelected = i != 0;
        });
    }
}

// ── 重複掃描頁面 ViewModel ──────────────────────────────────────
public partial class DuplicateScanViewModel : INotifyPropertyChanged
{
    // 比對尾部的 (1)、(2)、(1) (1) 等序號後綴
    [GeneratedRegex(@"(\s*\(\d+\))+$")]
    private static partial Regex NumberSuffixRegex();

    private static readonly HashSet<string> AudioExts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".ogg", ".m4a", ".aac",
            ".wav", ".ape", ".wma", ".opus"
        };

    // ── 屬性 ────────────────────────────────────────────────────
    private string _scanFolder = string.Empty;
    public string ScanFolder
    {
        get => _scanFolder;
        set
        {
            SetField(ref _scanFolder, value);
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
        }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            SetField(ref _isScanning, value);
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }
    }

    private string _statusText = "尚未掃描。";
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        set
        {
            SetField(ref _selectedCount, value);
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = new();

    // ── 命令 ────────────────────────────────────────────────────
    public ICommand BrowseFolderCommand   { get; }
    public ICommand ScanCommand           { get; }
    public ICommand DeleteSelectedCommand { get; }

    public DuplicateScanViewModel()
    {
        BrowseFolderCommand   = new RelayCommand(BrowseFolder);
        ScanCommand           = new RelayCommand(StartScan,
            () => !IsScanning && !string.IsNullOrWhiteSpace(ScanFolder));
        DeleteSelectedCommand = new RelayCommand(DeleteSelected,
            () => !IsScanning && SelectedCount > 0);
    }

    // ── 瀏覽 ────────────────────────────────────────────────────
    private void BrowseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description            = "選擇要掃描的資料夾",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = false
        };
        if (!string.IsNullOrEmpty(ScanFolder) && Directory.Exists(ScanFolder))
            dialog.InitialDirectory = ScanFolder;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ScanFolder = dialog.SelectedPath;
    }

    // ── 掃描 ────────────────────────────────────────────────────
    private async void StartScan()
    {
        IsScanning = true;
        DuplicateGroups.Clear();
        SelectedCount = 0;
        StatusText    = "正在掃描，請稍候…";

        try
        {
            var groups = await Task.Run(() => FindDuplicates(ScanFolder));

            foreach (var g in groups)
            {
                foreach (var item in g.Items)
                    item.SelectionChanged = RecalcSelectedCount;
                DuplicateGroups.Add(g);
            }

            RecalcSelectedCount();

            int totalFiles      = groups.Sum(g => g.Items.Count);
            int duplicateCopies = groups.Sum(g => g.Items.Count - 1);
            StatusText = groups.Count == 0
                ? "✅  未發現重複檔案。"
                : $"發現 {groups.Count} 組重複，共 {totalFiles} 個檔案（建議可刪除 {duplicateCopies} 個副本）。";
        }
        catch (Exception ex)
        {
            StatusText = $"❌  掃描失敗：{ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void RecalcSelectedCount()
        => SelectedCount = DuplicateGroups.SelectMany(g => g.Items).Count(i => i.IsSelected);

    // ── 刪除 ────────────────────────────────────────────────────
    private async void DeleteSelected()
    {
        var toDelete = DuplicateGroups
            .SelectMany(g => g.Items)
            .Where(i => i.IsSelected)
            .Select(i => i.FilePath)
            .ToList();

        if (toDelete.Count == 0) return;

        var confirm = WpfMessageBox.Show(
            $"確定要永久刪除 {toDelete.Count} 個檔案嗎？\n\n此操作無法復原。",
            "確認刪除",
            WpfMessageBoxButton.YesNo,
            WpfMessageBoxImage.Warning,
            WpfMessageBoxResult.No);

        if (confirm != WpfMessageBoxResult.Yes) return;

        IsScanning = true;
        StatusText = $"正在刪除 {toDelete.Count} 個檔案…";

        int deleted = 0, failed = 0;
        await Task.Run(() =>
        {
            foreach (var path in toDelete)
            {
                try { File.Delete(path); deleted++; }
                catch { failed++; }
            }
        });

        // 從清單中移除已刪除的項目，群組剩不到 2 個則一併移除
        var deletedSet = new HashSet<string>(toDelete, StringComparer.OrdinalIgnoreCase);
        foreach (var g in DuplicateGroups.ToList())
        {
            foreach (var item in g.Items.Where(i => deletedSet.Contains(i.FilePath)).ToList())
                g.Items.Remove(item);
            if (g.Items.Count < 2)
                DuplicateGroups.Remove(g);
        }

        SelectedCount = 0;
        IsScanning    = false;
        StatusText = failed == 0
            ? $"✅  已成功刪除 {deleted} 個檔案。"
            : $"⚠  刪除完成：成功 {deleted} 個，失敗 {failed} 個。";
    }

    // ── 核心演算法 ───────────────────────────────────────────────
    private static List<DuplicateGroupViewModel> FindDuplicates(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => AudioExts.Contains(Path.GetExtension(f)))
            .ToList();

        var keyToFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var keyToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var (key, label) = GetFileKeyAndLabel(file);
            if (!keyToFiles.TryGetValue(key, out var list))
            {
                list = new List<string>();
                keyToFiles[key] = list;
                keyToLabel[key] = label;
            }
            list.Add(file);
        }

        var result = new List<DuplicateGroupViewModel>();
        foreach (var kvp in keyToFiles)
        {
            if (kvp.Value.Count < 2) continue;

            var g = new DuplicateGroupViewModel { GroupLabel = keyToLabel[kvp.Key] };

            // 沒有 (N) 後綴的檔案排前面（可能是「原始檔案」）
            var sorted = kvp.Value
                .OrderBy(f => HasNumberSuffix(Path.GetFileNameWithoutExtension(f)) ? 1 : 0)
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in sorted)
            {
                g.Items.Add(new DuplicateFileItem
                {
                    FilePath    = file,
                    DisplaySize = FormatSize(new FileInfo(file).Length),
                    // 預設勾選：檔名帶有 (N) 序號的為疑似副本
                    IsSelected  = HasNumberSuffix(Path.GetFileNameWithoutExtension(file))
                });
            }

            result.Add(g);
        }

        return result.OrderBy(g => g.GroupLabel, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 取得檔案的群組 key 及顯示標籤。
    /// 優先讀取 ATL 嵌入標籤（歌名＋歌手），無標籤時改用去除序號後綴的檔名。
    /// </summary>
    private static (string key, string label) GetFileKeyAndLabel(string filePath)
    {
        try
        {
            var track  = new Track(filePath);
            string title  = (track.Title  ?? "").Trim();
            string artist = (track.Artist ?? "").Trim();

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist))
                return ($"META:{title.ToLowerInvariant()}|{artist.ToLowerInvariant()}",
                        $"🎵 {title}  ·  🎤 {artist}");

            if (!string.IsNullOrEmpty(title))
                return ($"TITLE:{title.ToLowerInvariant()}",
                        $"🎵 {title}");
        }
        catch { /* ATL 讀取失敗，改用檔名判斷 */ }

        string stem    = Path.GetFileNameWithoutExtension(filePath);
        string cleaned = NumberSuffixRegex().Replace(stem, "").Trim();
        return ($"FILE:{cleaned.ToLowerInvariant()}",
                $"📄 {cleaned}");
    }

    private static bool HasNumberSuffix(string stem)
        => NumberSuffixRegex().IsMatch(stem);

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B"
    };

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
