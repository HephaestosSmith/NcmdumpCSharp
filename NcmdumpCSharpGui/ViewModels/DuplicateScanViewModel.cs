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
    public string FilePath       { get; init; } = string.Empty;
    public string FileName       => Path.GetFileName(FilePath);
    public long   FileSize       { get; init; }
    public string DisplaySize    { get; init; } = string.Empty;
    public string DisplayBitrate { get; init; } = string.Empty;
    public string DisplayFormat  { get; init; } = string.Empty;
    public bool   IsLargest      { get; init; }
    public string FolderPath     => Path.GetDirectoryName(FilePath) ?? string.Empty;

    /// <summary>格式與位元率的組合顯示字串，例如「FLAC · 320 kbps」</summary>
    public string DisplayMeta =>
        (DisplayFormat, DisplayBitrate) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{DisplayFormat} · {DisplayBitrate}",
            ({ Length: > 0 }, _)               => DisplayFormat,
            (_, { Length: > 0 })               => DisplayBitrate,
            _                                  => string.Empty
        };

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
    private record FileEntry(
        string FilePath, long Size, int Bitrate, string Format, string Key, string Label);

    private static List<DuplicateGroupViewModel> FindDuplicates(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => AudioExts.Contains(Path.GetExtension(f)))
            .ToList();

        // key → (條目清單, 群組標籤)
        var groups = new Dictionary<string, (List<FileEntry> Entries, string Label)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var entry = BuildFileEntry(file);
            if (!groups.TryGetValue(entry.Key, out var bucket))
            {
                bucket = (new List<FileEntry>(), entry.Label);
                groups[entry.Key] = bucket;
            }
            bucket.Entries.Add(entry);
        }

        var result = new List<DuplicateGroupViewModel>();
        foreach (var kvp in groups)
        {
            if (kvp.Value.Entries.Count < 2) continue;

            var g = new DuplicateGroupViewModel { GroupLabel = kvp.Value.Label };

            // 以檔案大小降冪排列，最大的排第一（優先保留）
            var sorted = kvp.Value.Entries.OrderByDescending(e => e.Size).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var e = sorted[i];
                g.Items.Add(new DuplicateFileItem
                {
                    FilePath       = e.FilePath,
                    FileSize       = e.Size,
                    DisplaySize    = FormatSize(e.Size),
                    DisplayBitrate = e.Bitrate > 0 ? $"{e.Bitrate} kbps" : string.Empty,
                    DisplayFormat  = e.Format,
                    IsLargest      = i == 0,
                    // 最大的（第一個）預設不勾選；其餘預設勾選為待刪除
                    IsSelected     = i != 0
                });
            }

            result.Add(g);
        }

        return result.OrderBy(g => g.GroupLabel, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 讀取檔案的音訊 metadata，建構群組比對用的 FileEntry（一次 ATL 呼叫）。
    /// 優先讀取嵌入標籤（歌名＋歌手），無標籤時改用去除序號後綴的檔名。
    /// </summary>
    private static FileEntry BuildFileEntry(string filePath)
    {
        long   size    = new FileInfo(filePath).Length;
        int    bitrate = 0;
        string format  = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();

        try
        {
            var track = new Track(filePath);
            string title  = (track.Title  ?? "").Trim();
            string artist = (track.Artist ?? "").Trim();
            if (track.Bitrate > 0) bitrate = track.Bitrate;

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist))
                return new FileEntry(filePath, size, bitrate, format,
                    $"META:{title.ToLowerInvariant()}|{artist.ToLowerInvariant()}",
                    $"🎵 {title}  ·  🎤 {artist}");

            if (!string.IsNullOrEmpty(title))
                return new FileEntry(filePath, size, bitrate, format,
                    $"TITLE:{title.ToLowerInvariant()}",
                    $"🎵 {title}");
        }
        catch { /* ATL 讀取失敗，改用檔名判斷 */ }

        string stem    = Path.GetFileNameWithoutExtension(filePath);
        string cleaned = NumberSuffixRegex().Replace(stem, "").Trim();
        return new FileEntry(filePath, size, bitrate, format,
            $"FILE:{cleaned.ToLowerInvariant()}",
            $"📄 {cleaned}");
    }

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
