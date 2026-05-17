namespace NcmdumpCSharpGui.Models;

public enum LogLevel
{
    資訊,
    成功,
    警告,
    錯誤
}

public class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Message { get; init; } = string.Empty;
    public LogLevel Level { get; init; } = LogLevel.資訊;
    public string TimeString => Time.ToString("HH:mm:ss");
}
