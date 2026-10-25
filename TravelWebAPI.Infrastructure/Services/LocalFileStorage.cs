using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Content;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>Settings of the upload folder (<c>Storage</c> section).</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>
    /// Folder that holds uploaded images. A relative path is resolved against the application folder.
    /// In production this must be on storage that survives restarts and redeploys.
    /// </summary>
    public string UploadsPath { get; set; } = "uploads";
}

/// <summary>Keeps uploaded files in one folder on disk. File names come from the caller, never from the client.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private string Folder => Path.GetFullPath(options.Value.UploadsPath);

    /// <summary>Creates the folder and proves it can be written to, so a bad path stops start-up instead of failing the first upload.</summary>
    public static void EnsureReady(string folder)
    {
        var path = Path.GetFullPath(folder);
        Directory.CreateDirectory(path);

        var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllBytes(probe, []);
        File.Delete(probe);
    }

    public async Task SaveAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        var path = PathFor(fileName);

        // CreateNew: a name that already exists is an error, never an overwrite.
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await stream.WriteAsync(content, cancellationToken);
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(fileName));
        return Task.CompletedTask;
    }

    /// <summary>The full path of a stored file. Only a plain file name is accepted, so nothing can point outside the folder.</summary>
    private string PathFor(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName) || fileName.StartsWith('.'))
        {
            throw new ArgumentException("A stored file name has no folders.", nameof(fileName));
        }

        return Path.Combine(Folder, fileName);
    }
}
