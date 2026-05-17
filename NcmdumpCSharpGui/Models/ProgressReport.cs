namespace NcmdumpCSharpGui.Models;

/// <summary>
/// 進度回報
/// </summary>
/// <param name="Processed">已完成數量</param>
/// <param name="Total">總數量</param>
/// <param name="CurrentFile">當前處理中的檔案相對路徑</param>
public record ProgressReport(int Processed, int Total, string CurrentFile);
