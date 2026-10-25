using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Content;

/// <summary>Where uploaded image files are kept. Today a folder on disk; blob storage can replace it without changing the URLs.</summary>
public interface IFileStorage
{
    /// <summary>Stores the bytes under <paramref name="fileName"/>, which the caller generated. Fails if the name is taken.</summary>
    Task SaveAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>Removes a file; a file that is not there is not an error.</summary>
    Task DeleteAsync(string fileName, CancellationToken cancellationToken = default);
}

/// <summary>The answer to a successful upload: the public path of the file.</summary>
public sealed record UploadResponse(string Url);

/// <summary>An image type the API accepts, decided from the first bytes of the file.</summary>
public sealed record ImageKind(string Extension, string ContentType)
{
    public static readonly ImageKind Jpeg = new(".jpg", "image/jpeg");
    public static readonly ImageKind Png = new(".png", "image/png");
    public static readonly ImageKind Webp = new(".webp", "image/webp");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>JPEG, PNG or WebP by signature; null for anything else. The file name and declared type are never used.</summary>
    public static ImageKind? Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return Jpeg;
        }

        if (content.Length >= PngSignature.Length && content[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return Png;
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }
}

/// <summary>Admin image uploads: size and type checks, a generated file name, storage and the media record.</summary>
public sealed class UploadService(IFileStorage storage, IRepository<MediaFile> media, IUnitOfWork unitOfWork, ICurrentUser currentUser)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxOriginalNameLength = 200;
    public const string MissingFileMessage = "Choose an image to upload";
    public const string WrongTypeMessage = "Only JPEG, PNG or WebP images are allowed";

    /// <summary>Reads at most <see cref="MaxBytes"/> + 1 bytes, so an oversized file is noticed without reading all of it.</summary>
    public async Task<UploadResponse> UploadAsync(string? originalName, Stream content, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadLimitedAsync(content, cancellationToken);

        if (bytes.Length == 0)
        {
            throw new ValidationException("file", MissingFileMessage);
        }

        if (bytes.Length > MaxBytes)
        {
            throw new PayloadTooLargeException();
        }

        var kind = ImageKind.Detect(bytes) ?? throw new ValidationException("file", WrongTypeMessage);

        var fileName = $"{Guid.CreateVersion7():N}{kind.Extension}";
        await storage.SaveAsync(fileName, bytes, cancellationToken);

        try
        {
            await media.AddAsync(
                new MediaFile
                {
                    FileName = fileName,
                    OriginalName = CleanFileName(originalName),
                    ContentType = kind.ContentType,
                    SizeBytes = bytes.Length,
                    UploadedBy = currentUser.UserId,
                },
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // No record means nothing points to the file, so do not leave it behind.
            await storage.DeleteAsync(fileName, CancellationToken.None);
            throw;
        }

        return new UploadResponse(ImageReference.UploadsPrefix + fileName);
    }

    /// <summary>The client's file name without any folders or control characters, shortened to fit the record.</summary>
    public static string CleanFileName(string? name)
    {
        var cleaned = (name ?? string.Empty).Replace('\\', '/');
        cleaned = cleaned[(cleaned.LastIndexOf('/') + 1)..];
        cleaned = new string([.. cleaned.Where(c => !char.IsControl(c))]).Trim();

        if (cleaned.Length > MaxOriginalNameLength)
        {
            cleaned = cleaned[..MaxOriginalNameLength];
        }

        return cleaned.Length == 0 ? "upload" : cleaned;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;

        while (buffer.Length <= MaxBytes && (read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
