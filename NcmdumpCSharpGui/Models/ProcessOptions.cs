namespace NcmdumpCSharpGui.Models;

/// <summary>
/// 批次處理選項
/// </summary>
public class ProcessOptions
{
    public string SourceFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>將檔名及嵌入標籤轉換為繁體中文</summary>
    public bool TranslateToTraditional { get; set; }

    /// <summary>複製非 NCM 檔案至輸出資料夾</summary>
    public bool CopyNonNcmFiles { get; set; }

    /// <summary>平行執行緒數量</summary>
    public int MaxDegreeOfParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
}
