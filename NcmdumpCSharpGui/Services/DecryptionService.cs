using ATL;
using NcmdumpCSharp.Core;
using NcmdumpCSharpGui.Models;

namespace NcmdumpCSharpGui.Services;

/// <summary>
/// NCM 解密服務：掃描資料夾、多執行緒解密、複製非 NCM 檔案。
/// </summary>
public class DecryptionService
{
    /// <summary>
    /// 非同步批次處理來源資料夾內的所有檔案。
    /// </summary>
    /// <param name="options">處理選項</param>
    /// <param name="progress">進度回報（在 UI 執行緒上觸發）</param>
    /// <param name="log">記錄回報（在 UI 執行緒上觸發）</param>
    /// <param name="cancellationToken">取消 Token</param>
    /// <returns>(總數, 成功數, 失敗數)</returns>
    public async Task<(int Total, int Succeeded, int Failed)> ProcessAsync(
        ProcessOptions options,
        IProgress<ProgressReport>? progress,
        IProgress<LogEntry>? log,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(options.SourceFolder))
            throw new DirectoryNotFoundException($"來源資料夾不存在：{options.SourceFolder}");

        Directory.CreateDirectory(options.OutputFolder);

        // ── 掃描階段 ──────────────────────────────────────────────
        var ncmFiles = Directory
            .GetFiles(options.SourceFolder, "*.ncm", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        var otherFiles = new List<string>();
        if (options.CopyNonNcmFiles)
        {
            otherFiles = Directory
                .GetFiles(options.SourceFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".ncm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();
        }

        int total = ncmFiles.Count + otherFiles.Count;

        if (total == 0)
        {
            log?.Report(new LogEntry { Message = "⚠ 未找到任何可處理的檔案。", Level = LogLevel.警告 });
            return (0, 0, 0);
        }

        log?.Report(new LogEntry
        {
            Message = $"掃描完成：找到 {ncmFiles.Count} 個 NCM 檔案" +
                      (options.CopyNonNcmFiles ? $"，{otherFiles.Count} 個其他檔案" : string.Empty) +
                      $"，共 {total} 個。",
            Level = LogLevel.資訊
        });

        // 先回報總數，讓 UI 能顯示正確的分母
        progress?.Report(new ProgressReport(0, total, string.Empty));

        // ── 處理階段 ──────────────────────────────────────────────
        int processedCount = 0;
        int succeededCount = 0;
        int failedCount    = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken      = cancellationToken
        };

        // 處理 NCM 檔案
        await Parallel.ForEachAsync(ncmFiles, parallelOptions, async (filePath, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            string relPath = Path.GetRelativePath(options.SourceFolder, filePath);
            progress?.Report(new ProgressReport(processedCount, total, relPath));

            var entry = await Task.Run(() => ProcessNcmFile(filePath, options), ct);

            int cur = Interlocked.Increment(ref processedCount);
            if (entry.Level == LogLevel.成功) Interlocked.Increment(ref succeededCount);
            else                              Interlocked.Increment(ref failedCount);

            progress?.Report(new ProgressReport(cur, total, relPath));
            log?.Report(entry);
        });

        // 複製非 NCM 檔案
        await Parallel.ForEachAsync(otherFiles, parallelOptions, async (filePath, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            string relPath = Path.GetRelativePath(options.SourceFolder, filePath);
            progress?.Report(new ProgressReport(processedCount, total, relPath));

            var entry = await Task.Run(() => CopyOtherFile(filePath, options), ct);

            int cur = Interlocked.Increment(ref processedCount);
            if (entry.Level == LogLevel.成功) Interlocked.Increment(ref succeededCount);
            else                              Interlocked.Increment(ref failedCount);

            progress?.Report(new ProgressReport(cur, total, relPath));
            log?.Report(entry);
        });

        return (total, succeededCount, failedCount);
    }

    // ── 私有輔助方法 ──────────────────────────────────────────────

    private string GetOutputDir(string filePath, ProcessOptions options)
    {
        string relativeDir = Path.GetDirectoryName(
            Path.GetRelativePath(options.SourceFolder, filePath)) ?? string.Empty;

        if (options.TranslateToTraditional && !string.IsNullOrEmpty(relativeDir))
        {
            var segments = relativeDir.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.None);
            relativeDir = Path.Combine(
                segments.Select(s => ChineseConverter.ToTraditional(s)).ToArray());
        }

        return Path.Combine(options.OutputFolder, relativeDir);
    }

    private LogEntry ProcessNcmFile(string filePath, ProcessOptions options)
    {
        try
        {
            string outputDir = GetOutputDir(filePath, options);
            Directory.CreateDirectory(outputDir);

            using var crypt = new NeteaseCrypt(filePath);

            // 在寫入前先翻譯 metadata 標籤
            if (options.TranslateToTraditional && crypt.Metadata != null)
            {
                crypt.Metadata.Name   = ChineseConverter.ToTraditional(crypt.Metadata.Name);
                crypt.Metadata.Artist = ChineseConverter.ToTraditional(crypt.Metadata.Artist);
                crypt.Metadata.Album  = ChineseConverter.ToTraditional(crypt.Metadata.Album);
            }

            // 解密音訊資料並寫入檔案
            crypt.Dump(outputDir);

            // 將翻譯後的標籤嵌入音訊檔案
            crypt.FixMetadata();

            // 翻譯輸出檔案的所有嵌入標籤（含音訊串流原有的其他欄位）
            if (options.TranslateToTraditional)
                TranslateFileTags(crypt.DumpFilePath);

            // 重新命名輸出檔案為繁體中文檔名
            string finalPath = crypt.DumpFilePath;
            if (options.TranslateToTraditional)
                finalPath = RenameToTraditional(crypt.DumpFilePath);

            string displayName = Path.GetFileName(finalPath);
            return new LogEntry
            {
                Message = $"✓  {displayName}",
                Level   = LogLevel.成功
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LogEntry
            {
                Message = $"✗  {Path.GetFileName(filePath)}  —  {ex.Message}",
                Level   = LogLevel.錯誤
            };
        }
    }

    private LogEntry CopyOtherFile(string filePath, ProcessOptions options)
    {
        try
        {
            string outputDir = GetOutputDir(filePath, options);
            Directory.CreateDirectory(outputDir);

            string srcName  = Path.GetFileName(filePath);
            string destName = options.TranslateToTraditional
                ? ChineseConverter.ToTraditional(srcName)
                : srcName;

            string destPath = Path.Combine(outputDir, destName);
            File.Copy(filePath, destPath, overwrite: true);

            // 翻譯複製檔案的嵌入標籤（標題、歌手、專輯等）
            if (options.TranslateToTraditional)
                TranslateFileTags(destPath);

            return new LogEntry
            {
                Message = $"↷  {destName}  （已複製）",
                Level   = LogLevel.成功
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LogEntry
            {
                Message = $"✗  {Path.GetFileName(filePath)}  —  {ex.Message}",
                Level   = LogLevel.錯誤
            };
        }
    }

    /// <summary>
    /// 讀取音訊檔案的嵌入標籤，將所有文字欄位
    /// 從簡體中文轉換為繁體中文後存回檔案。
    /// 不支援標籤的格式（或標籤全為空）會直接略過。
    /// </summary>
    private static void TranslateFileTags(string filePath)
    {
        try
        {
            var tag = new Track(filePath);

            bool changed = false;

            string? t(string? s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                string converted = ChineseConverter.ToTraditional(s);
                if (converted != s) changed = true;
                return converted;
            }

            tag.Title          = t(tag.Title);
            tag.Artist         = t(tag.Artist);
            tag.Album          = t(tag.Album);
            tag.AlbumArtist    = t(tag.AlbumArtist);
            tag.Composer       = t(tag.Composer);
            tag.Comment        = t(tag.Comment);
            tag.Description    = t(tag.Description);
            tag.Copyright      = t(tag.Copyright);
            tag.Publisher      = t(tag.Publisher);
            tag.OriginalArtist = t(tag.OriginalArtist);
            tag.OriginalAlbum  = t(tag.OriginalAlbum);
            tag.Conductor      = t(tag.Conductor);
            tag.Lyricist       = t(tag.Lyricist);

            // 內嵌歌詞（含時間軸歌詞的文字部分）
            if (tag.Lyrics.Count > 0)
            {
                foreach (var lyric in tag.Lyrics)
                {
                    if (!string.IsNullOrEmpty(lyric.UnsynchronizedLyrics))
                    {
                        string converted = ChineseConverter.ToTraditional(lyric.UnsynchronizedLyrics);
                        if (converted != lyric.UnsynchronizedLyrics)
                        {
                            lyric.UnsynchronizedLyrics = converted;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
                tag.Save();
        }
        catch
        {
            // 無法讀寫標籤時靜默略過，不影響複製結果
        }
    }

    /// <summary>
    /// 將 <paramref name="filePath"/> 的檔名部分轉換為繁體中文並重新命名。
    /// 若來源與目標相同則不做任何動作。
    /// </summary>
    private static string RenameToTraditional(string filePath)
    {
        string dir           = Path.GetDirectoryName(filePath) ?? string.Empty;
        string ext           = Path.GetExtension(filePath);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string translatedName = ChineseConverter.ToTraditional(nameWithoutExt);
        string newPath        = Path.Combine(dir, translatedName + ext);

        if (string.Equals(filePath, newPath, StringComparison.OrdinalIgnoreCase))
            return filePath;

        if (!File.Exists(filePath))
            return filePath;

        // 若目標已存在先刪除
        if (File.Exists(newPath))
            File.Delete(newPath);

        File.Move(filePath, newPath);
        return newPath;
    }
}
