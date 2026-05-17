using System.Text.Json;

namespace NcmdumpCSharp.Models;

/// <summary>
///     網易雲音樂元數據
/// </summary>
public class NeteaseMusicMetadata
{
    public string Name { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public int Duration { get; set; }

    public int Bitrate { get; set; }

    /// <summary>
    ///     從 JSON字串解析元數據
    /// </summary>
    /// <param name="jsonString">JSON字串</param>
    /// <returns>元數據物件</returns>
    public static NeteaseMusicMetadata? FromJson(string jsonString)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonString);
            var root = document.RootElement;

            var metadata = new NeteaseMusicMetadata();

            if (root.TryGetProperty("musicName", out var musicName))
            {
                metadata.Name = musicName.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("album", out var album))
            {
                metadata.Album = album.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("artist", out var artist))
            {
                if (artist.ValueKind == JsonValueKind.Array)
                {
                    var artists = artist
                        .EnumerateArray()
                        .Where(artistItem =>
                            artistItem.ValueKind == JsonValueKind.Array && artistItem.GetArrayLength() > 0
                        )
                        .Select(artistItem => artistItem[0])
                        .Where(firstArtist => firstArtist.ValueKind == JsonValueKind.String)
                        .Select(firstArtist => firstArtist.GetString() ?? string.Empty)
                        .ToList();

                    metadata.Artist = string.Join("/", artists);
                }
            }

            if (root.TryGetProperty("bitrate", out var bitrate))
            {
                metadata.Bitrate = bitrate.GetInt32();
            }

            if (root.TryGetProperty("duration", out var duration))
            {
                metadata.Duration = duration.GetInt32();
            }

            if (root.TryGetProperty("format", out var format))
            {
                metadata.Format = format.GetString() ?? string.Empty;
            }

            return metadata;
        }
        catch
        {
            return null;
        }
    }
}
