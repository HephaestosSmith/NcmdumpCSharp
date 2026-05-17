using System.IO;
using System.Text;
using ATL;
using NcmdumpCSharp.Crypto;
using NcmdumpCSharp.Models;

namespace NcmdumpCSharp.Core;

/// <summary>
///     網易雲音樂NCM檔案解密器
/// </summary>
public class NeteaseCrypt : IDisposable
{
    // 固定的金鑰
    private static readonly byte[] _coreKey = "hzHRAmso5kInbaxW"u8.ToArray();
    private static readonly byte[] _modifyKey = "#14ljk_!\\]&0U<'("u8.ToArray();
    private static readonly byte[] _pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _filePath;
    private readonly byte[] _keyBox = new byte[256];
    private FileStream? _fileStream;

    /// <summary>
    ///     建構函式
    /// </summary>
    /// <param name="filePath">NCM檔案路徑</param>
    public NeteaseCrypt(string filePath)
    {
        _filePath = filePath;
        Initialize();
    }

    /// <summary>
    ///     解析自 NCM 的元數據物件（標題、藝術家、專輯、格式等）。
    ///     初始化時從 NCM 標頭讀取；在解密首個音訊區塊時如未確定 Format 將補全。
    /// </summary>
    public NeteaseMusicMetadata? Metadata { get; private set; }

    /// <summary>
    ///     專輯封面二進位資料（JPEG 或 PNG），可在 <see cref="FixMetadata" /> 時寫入音訊標籤。
    /// </summary>
    public byte[]? ImageData { get; private set; }

    /// <summary>
    ///     取得輸出檔案路徑
    /// </summary>
    public string DumpFilePath { get; private set; } = string.Empty;

    /// <summary>
    ///     釋放資源
    /// </summary>
    public void Dispose()
    {
        _fileStream?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     初始化解密器
    /// </summary>
    private void Initialize()
    {
        if (!File.Exists(_filePath))
        {
            throw new FileNotFoundException($"檔案不存在：{_filePath}");
        }

        _fileStream = File.OpenRead(_filePath);

        if (!IsNcmFile())
        {
            throw new InvalidOperationException("不是有效的網易雲音樂NCM檔案");
        }

        // 跳過檔案標頭
        _fileStream.Seek(2, SeekOrigin.Current);

        // 讀取金鑰資料長度
        int keyDataLength = ReadInt32();

        if (keyDataLength <= 0)
        {
            throw new InvalidOperationException("損壞的NCM檔案");
        }

        // 讀取金鑰資料
        byte[] keyData = new byte[keyDataLength];
        ReadBytes(keyData, 0, keyDataLength);

        // 異或解密
        for (int i = 0; i < keyDataLength; i++)
        {
            keyData[i] ^= 0x64;
        }

        // AES解密
        byte[] decryptedKeyData = AesHelper.AesEcbDecrypt(_coreKey, keyData);

        // 构建密钥盒
        BuildKeyBox(decryptedKeyData, 17, decryptedKeyData.Length - 17);

        // 讀取元數據長度
        int metadataLength = ReadInt32();

        if (metadataLength > 0)
        {
            // 讀取元數據
            byte[] modifyData = new byte[metadataLength];
            ReadBytes(modifyData, 0, metadataLength);

            // 異或解密
            for (int i = 0; i < metadataLength; i++)
            {
                modifyData[i] ^= 0x63;
            }

            // 跳过"163 key(Don'\''t modify):"
            string swapModifyData = Encoding.UTF8.GetString(modifyData, 22, modifyData.Length - 22);

            // Base64解碼
            byte[] modifyOutData = Base64Helper.Decode(swapModifyData);

            // AES解密
            byte[] modifyDecryptData = AesHelper.AesEcbDecrypt(_modifyKey, modifyOutData);

            // 跳过"music:"
            string jsonData = Encoding.UTF8.GetString(modifyDecryptData, 6, modifyDecryptData.Length - 6);

            Metadata = NeteaseMusicMetadata.FromJson(jsonData);
        }

        // 跳過CRC32及圖片版本
        _fileStream.Seek(5, SeekOrigin.Current);

        // 讀取封面長度
        int coverFrameLength = ReadInt32();
        int imageLength = ReadInt32();

        if (imageLength > 0)
        {
            ImageData = new byte[imageLength];
            ReadBytes(ImageData, 0, imageLength);
        }

        // 跳過剩餘的封面資料
        _fileStream.Seek(coverFrameLength - imageLength, SeekOrigin.Current);
    }

    /// <summary>
    ///     檢查是否為NCM檔案
    /// </summary>
    private bool IsNcmFile()
    {
        int header1 = ReadInt32();
        int header2 = ReadInt32();

        return header1 == 0x4e455443 && header2 == 0x4d414446;
    }

    /// <summary>
    ///     建構金鑰盒
    /// </summary>
    private void BuildKeyBox(byte[] key, int keyOffset, int keyLength)
    {
        // 初始化金鑰盒
        for (int i = 0; i < 256; i++)
        {
            _keyBox[i] = (byte)i;
        }

        byte lastByte = 0;
        byte keyOffset2 = 0;

        for (int i = 0; i < 256; i++)
        {
            byte swap = _keyBox[i];
            byte c = (byte)(swap + lastByte + key[keyOffset + keyOffset2] & 0xff);
            keyOffset2++;

            if (keyOffset2 >= keyLength)
                keyOffset2 = 0;

            _keyBox[i] = _keyBox[c];
            _keyBox[c] = swap;
            lastByte = c;
        }
    }

    /// <summary>
    ///     讀取4位元組整數
    /// </summary>
    private int ReadInt32()
    {
        byte[] buffer = new byte[4];
        ReadBytes(buffer, 0, 4);

        return BitConverter.ToInt32(buffer, 0);
    }

    /// <summary>
    ///     讀取位元組陣列
    /// </summary>
    private void ReadBytes(byte[] buffer, int offset, int count)
    {
        if (_fileStream == null)
            throw new InvalidOperationException("檔案串流未初始化");

        int bytesRead = _fileStream.Read(buffer, offset, count);

        if (bytesRead != count)
        {
            throw new InvalidOperationException("讀取檔案失敗");
        }
    }

    /// <summary>
    ///     取得MIME類型
    /// </summary>
    public static string GetMimeType(byte[] data)
    {
        if (data.Length >= 8 && data.Take(8).SequenceEqual(_pngHeader))
        {
            return "image/png";
        }

        return "image/jpeg";
    }

    /// <summary>
    ///     使用金鑰盒對緩衝區執行 RC4 異或解密，並推進解密位置。
    /// </summary>
    /// <param name="buffer">就地解密的資料緩衝區</param>
    /// <param name="position">檔案內偏移位置（將被按已處理位元組數遞增）</param>
    private void Rc4Xor(Span<byte> buffer, ref long position)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            int j = (int)(position + i + 1 & 0xff);
            buffer[i] ^= _keyBox[_keyBox[j] + _keyBox[_keyBox[j] + j & 0xff] & 0xff];
        }

        position += buffer.Length;
    }

    /// <summary>
    ///     從首塊資料前綴判斷音訊格式（mp3/flac），無法辨識時返回 null。
    /// </summary>
    /// <param name="buffer">首塊資料的唯讀切片</param>
    /// <returns>檔案副檔名（不含點），或 null</returns>
    private static string? DetectFormat(ReadOnlySpan<byte> buffer)
    {
        return buffer.Length switch
        {
            // ID3 -> MP3
            >= 3 when buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33 => "mp3",

            // fLaC -> FLAC
            >= 4 when buffer[0] == 0x66 && buffer[1] == 0x4C && buffer[2] == 0x61 && buffer[3] == 0x43 => "flac",
            _ => null,
        };
    }

    /// <summary>
    ///     準備基礎輸出路徑（不含副檔名），用於 Dump/DumpAsync。
    /// </summary>
    /// <param name="outputDir">可選的輸出目錄；為空時與來源檔案同目錄</param>
    private void PrepareDumpBasePath(string? outputDir)
    {
        if (string.IsNullOrEmpty(outputDir))
        {
            DumpFilePath = Path.ChangeExtension(_filePath, null);
        }
        else
        {
            string fileName = Path.GetFileNameWithoutExtension(_filePath);
            DumpFilePath = Path.Combine(outputDir, fileName);
        }
    }

    /// <summary>
    ///     對資料區塊做 RC4 解密，並在首個資料區塊時偵測音訊格式（設定 <see cref="NeteaseMusicMetadata.Format" />）。
    /// </summary>
    /// <param name="span">待就地解密的資料區塊</param>
    /// <param name="position">檔案內偏移位置（將被按已處理位元組數遞增）</param>
    /// <param name="firstChunk">是否為首個資料區塊（呼叫內維護並在首次後置為 false）</param>
    private void DecryptAndMaybeDetectFormat(Span<byte> span, ref long position, ref bool firstChunk)
    {
        Rc4Xor(span, ref position);

        if (!firstChunk)
            return;

        if (string.IsNullOrEmpty(Metadata?.Format))
        {
            Metadata ??= new NeteaseMusicMetadata();
            Metadata.Format = DetectFormat(span) ?? Metadata.Format;
        }

        firstChunk = false;
    }

    /// <summary>
    ///     基於首塊資料確定最終輸出路徑（含副檔名）並建立輸出串流。
    /// </summary>
    /// <param name="firstChunk">首塊資料，用於格式偵測</param>
    /// <param name="useAsync">是否以非同步寫入模式開啟檔案串流</param>
    /// <returns>已建立的檔案寫入串流</returns>
    private FileStream CreateOutputStreamForFirstChunk(ReadOnlySpan<byte> firstChunk, bool useAsync)
    {
        string? fmt = DetectFormat(firstChunk);
        // 使用字串串接而非 Path.ChangeExtension，避免檔名中含有 '.' 時
        // （例如 "[.que] - Teardrops" 或 "(Piano ver.)"）被誤判為副檔名而截斷。
        DumpFilePath = DumpFilePath + "." + (fmt ?? "flac");

        string? outputDir2 = Path.GetDirectoryName(DumpFilePath);

        if (!string.IsNullOrEmpty(outputDir2) && !Directory.Exists(outputDir2))
        {
            Directory.CreateDirectory(outputDir2);
        }

        return new FileStream(DumpFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 0x8000, useAsync);
    }

    /// <summary>
    ///     解密並將音訊寫入至檔案（同步）。
    /// </summary>
    /// <param name="outputDir">可選的輸出目錄；為空時預設寫入至來源檔案同目錄</param>
    public void Dump(string? outputDir = null)
    {
        PrepareDumpBasePath(outputDir);

        byte[] buffer = new byte[0x8000];
        FileStream? outputStream = null;
        long position = 0;
        bool firstChunk = true;

        try
        {
            int bytesRead;

            while (_fileStream != null && (bytesRead = _fileStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                var span = buffer.AsSpan(0, bytesRead);
                DecryptAndMaybeDetectFormat(span, ref position, ref firstChunk);

                outputStream ??= CreateOutputStreamForFirstChunk(span, false);

                outputStream.Write(span);
            }
        }
        finally
        {
            outputStream?.Close();
        }
    }

    /// <summary>
    ///     修復輸出音訊檔案的元數據（標題/藝術家/專輯/封面）。
    /// </summary>
    /// <exception cref="InvalidOperationException">當輸出檔案不存在或路徑為空時拋出</exception>
    public void FixMetadata()
    {
        if (string.IsNullOrEmpty(DumpFilePath) || !File.Exists(DumpFilePath))
        {
            throw new InvalidOperationException("輸出檔案不存在");
        }

        try
        {
            var tag = new Track(DumpFilePath);

            if (Metadata == null)
                return;

            tag.Title = Metadata.Name;
            tag.Artist = Metadata.Artist;
            tag.Album = Metadata.Album;

            // 添加封面圖片
            if (ImageData is { Length: > 0 })
            {
                var picture = PictureInfo.fromBinaryData(ImageData, PictureInfo.PIC_TYPE.Front);
                tag.EmbeddedPictures.Clear(); // 可選：清空已有封面
                tag.EmbeddedPictures.Add(picture);
            }

            tag.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 修復元數據失敗：{ex.Message}");
        }
    }

    /// <summary>
    ///     解密音訊資料至記憶體串流（同步）。
    /// </summary>
    /// <returns>包含解密後音訊資料的記憶體串流（Position 已重置為0）；當檔案串流未初始化時返回 null</returns>
    public MemoryStream? DumpToStream()
    {
        if (_fileStream == null)
        {
            return null;
        }

        var memoryStream = new MemoryStream();
        byte[] buffer = new byte[0x8000];
        int bytesRead;
        bool firstChunk = true;
        long position = 0;

        while ((bytesRead = _fileStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var span = buffer.AsSpan(0, bytesRead);
            DecryptAndMaybeDetectFormat(span, ref position, ref firstChunk);
            memoryStream.Write(span);
        }

        memoryStream.Position = 0;

        return memoryStream;
    }

    /// <summary>
    ///     解密音訊資料至記憶體串流（非同步）。
    /// </summary>
    /// <returns>包含解密後音訊資料的記憶體串流（Position 已重置為0）；當檔案串流未初始化時返回 null</returns>
    public async Task<MemoryStream?> DumpToStreamAsync()
    {
        if (_fileStream == null)
        {
            return null;
        }

        var memoryStream = new MemoryStream();
        byte[] buffer = new byte[0x8000];
        int bytesRead;
        bool firstChunk = true;
        long position = 0;

        while ((bytesRead = await _fileStream.ReadAsync(buffer)) > 0)
        {
            var span = buffer.AsSpan(0, bytesRead);
            DecryptAndMaybeDetectFormat(span, ref position, ref firstChunk);
            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }

        memoryStream.Position = 0;

        return memoryStream;
    }

    /// <summary>
    ///     解密並將音訊寫入至檔案（非同步）。
    /// </summary>
    /// <param name="outputDir">可選的輸出目錄；為空時預設寫入至來源檔案同目錄</param>
    public async Task DumpAsync(string? outputDir = null)
    {
        PrepareDumpBasePath(outputDir);

        byte[] buffer = new byte[0x8000];
        FileStream? outputStream = null;
        long position = 0;
        bool firstChunk = true;

        try
        {
            int bytesRead;

            while (_fileStream != null && (bytesRead = await _fileStream.ReadAsync(buffer)) > 0)
            {
                var span = buffer.AsSpan(0, bytesRead);
                DecryptAndMaybeDetectFormat(span, ref position, ref firstChunk);

                outputStream ??= CreateOutputStreamForFirstChunk(span, true);

                await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }
        }
        finally
        {
            if (outputStream is not null)
            {
                await outputStream.FlushAsync();
                outputStream.Close();
            }
        }
    }
}
