using System.Text;

namespace NcmdumpCSharp.Crypto;

/// <summary>
///     Base64解碼輔助類別
/// </summary>
public static class Base64Helper
{
    /// <summary>
    ///     Base64解碼
    /// </summary>
    /// <param name="base64String">Base64字串</param>
    /// <returns>解碼後的位元組陣列</returns>
    public static byte[] Decode(string base64String)
    {
        return Convert.FromBase64String(base64String);
    }

    /// <summary>
    ///     Base64解碼為字串
    /// </summary>
    /// <param name="base64String">Base64字串</param>
    /// <returns>解碼後的字串</returns>
    public static string DecodeToString(string base64String)
    {
        byte[] bytes = Decode(base64String);

        return Encoding.UTF8.GetString(bytes);
    }
}
