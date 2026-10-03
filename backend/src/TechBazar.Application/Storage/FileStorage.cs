namespace TechBazar.Application.Storage;

public interface IFileStorage
{
    /// <summary>Stores an uploaded image and returns the site-relative public URL (e.g. /uploads/products/2026/10/abc.webp).</summary>
    Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default);
}

/// <summary>Content-based image type detection: the client-supplied file name / content-type are never trusted.</summary>
public static class ImageSniffer
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Returns ".png/.jpg/.gif/.webp" from the first bytes, or null for anything else (SVG is deliberately refused: it can carry script).</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";
        if (header.Length >= 6 && header[..4].SequenceEqual("GIF8"u8) && (header[4] == (byte)'7' || header[4] == (byte)'9') && header[5] == (byte)'a') return ".gif";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}
