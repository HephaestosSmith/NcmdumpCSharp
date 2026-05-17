using System.Runtime.InteropServices;

namespace NcmdumpCSharpGui.Services;

/// <summary>
/// 使用 Windows 內建 LCMapStringEx API 將簡體中文轉換為繁體中文。
/// 不需要任何第三方套件，在所有 Windows 版本上均可使用。
/// </summary>
public static class ChineseConverter
{
    private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

    // 使用 IntPtr 作為輸出緩衝區，避免 StringBuilder marshaler
    // 掃到 NUL 字元就截斷字串的問題。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string? lpLocaleName,
        uint dwMapFlags,
        string lpSrcStr,
        int cchSrc,
        IntPtr lpDestStr,
        int cchDest,
        IntPtr lpVersionInformation,
        IntPtr lpReserved,
        IntPtr sortHandle);

    /// <summary>
    /// 將輸入字串從簡體中文轉換為繁體中文。
    /// 若輸入為空值或非中文字元，原樣傳回。
    /// </summary>
    public static string ToTraditional(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        int srcLen = input.Length;   // 不含 NUL，輸出大小也不含 NUL

        // 第一次呼叫：取得所需的輸出字元數
        int size = LCMapStringEx(
            null, LCMAP_TRADITIONAL_CHINESE,
            input, srcLen,
            IntPtr.Zero, 0,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (size <= 0)
            return input;

        // 配置非管理記憶體緩衝區（UTF-16，每字元 2 bytes）
        IntPtr buf = Marshal.AllocHGlobal(size * sizeof(char));
        try
        {
            int written = LCMapStringEx(
                null, LCMAP_TRADITIONAL_CHINESE,
                input, srcLen,
                buf, size,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (written <= 0)
                return input;

            // 明確指定輸出字元數，完全不依賴 NUL 終止
            return Marshal.PtrToStringUni(buf, written) ?? input;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}

