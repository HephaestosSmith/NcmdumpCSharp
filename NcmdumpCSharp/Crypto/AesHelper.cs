using System.Security.Cryptography;
using System.Text;

namespace NcmdumpCSharp.Crypto;

/// <summary>
///     AES解密輔助類別
/// </summary>
public static class AesHelper
{
    /// <summary>
    ///     AES ECB模式解密
    /// </summary>
    /// <param name="key">金鑰</param>
    /// <param name="encryptedData">加密資料</param>
    /// <returns>解密後的資料</returns>
    public static byte[] AesEcbDecrypt(byte[] key, byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;

        using var decryptor = aes.CreateDecryptor();

        return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
    }

    /// <summary>
    ///     AES ECB模式解密字串
    /// </summary>
    /// <param name="key">金鑰</param>
    /// <param name="encryptedString">加密字串</param>
    /// <returns>解密後的字串</returns>
    public static string AesEcbDecrypt(byte[] key, string encryptedString)
    {
        byte[] encryptedData = Encoding.UTF8.GetBytes(encryptedString);
        byte[] decryptedData = AesEcbDecrypt(key, encryptedData);

        return Encoding.UTF8.GetString(decryptedData);
    }
}
