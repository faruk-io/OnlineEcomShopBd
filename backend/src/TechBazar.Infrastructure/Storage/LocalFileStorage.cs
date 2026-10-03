using Microsoft.Extensions.Options;
using TechBazar.Application.Storage;

namespace TechBazar.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    /// <summary>Absolute folder for uploads. The API sets a default under its content root when empty.</summary>
    public string? RootPath { get; set; }
    public string RequestPath { get; set; } = "/uploads";
}

/// <summary>Stores uploads on local disk (dev / single server). Replace with blob storage + CDN for scale-out.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly StorageOptions _o = options.Value;

    public async Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        if (extension is not (".png" or ".jpg" or ".gif" or ".webp")) throw new ArgumentException("Unsupported extension.", nameof(extension));
        if (string.IsNullOrWhiteSpace(_o.RootPath)) throw new InvalidOperationException("Storage:RootPath is not configured.");

        var now = DateTime.UtcNow;
        var relative = Path.Combine("products", now.ToString("yyyy"), now.ToString("MM"));
        var folder = Path.Combine(_o.RootPath, relative);
        Directory.CreateDirectory(folder);

        var name = $"{Guid.NewGuid():N}{extension}"; // server-generated: the client file name never reaches the disk
        await using (var file = File.Create(Path.Combine(folder, name)))
            await content.CopyToAsync(file, ct);

        return $"{_o.RequestPath.TrimEnd('/')}/{relative.Replace('\\', '/')}/{name}";
    }
}
