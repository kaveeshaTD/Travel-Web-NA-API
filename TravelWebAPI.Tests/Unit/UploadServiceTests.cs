using System.Text;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class UploadServiceTests
{
    private readonly FakeStorage _storage = new();
    private readonly FakeMedia _media = new();
    private readonly FakeCurrentUser _user = new() { UserId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
    private readonly UploadService _service;

    public UploadServiceTests() => _service = new UploadService(_storage, _media, _media, _user);

    public static byte[] Jpeg(int length = 1_000) => Pad([0xFF, 0xD8, 0xFF, 0xE0], length);

    public static byte[] Png(int length = 1_000) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length);

    public static byte[] Webp(int length = 1_000) => Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8], length);

    private static byte[] Pad(byte[] start, int length)
    {
        var bytes = new byte[Math.Max(length, start.Length)];
        start.CopyTo(bytes, 0);
        return bytes;
    }

    private Task<UploadResponse> Upload(byte[] bytes, string? name = "photo.jpg") => _service.UploadAsync(name, new MemoryStream(bytes));

    // ---------------------------------------------------------------- types

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Each_allowed_type_is_stored_with_the_extension_of_its_content(string type)
    {
        var bytes = type switch { "jpg" => Jpeg(), "png" => Png(), _ => Webp() };

        var result = await Upload(bytes, "whatever.gif");

        Assert.Matches($"^/uploads/[0-9a-f]{{32}}\\.{type}$", result.Url);
        var stored = Assert.Single(_storage.Files);
        Assert.Equal(bytes, stored.Value);
        Assert.Equal(result.Url, "/uploads/" + stored.Key);
    }

    [Fact]
    public async Task The_media_record_has_the_detected_type_size_and_uploader()
    {
        await Upload(Png(2_345), "Holiday.PNG");

        var record = Assert.Single(_media.Added);
        Assert.Equal("image/png", record.ContentType);
        Assert.Equal(2_345, record.SizeBytes);
        Assert.Equal("Holiday.PNG", record.OriginalName);
        Assert.Equal(_user.UserId, record.UploadedBy);
        Assert.Equal(_storage.Files.Keys.Single(), record.FileName);
        Assert.Equal(1, _media.Saves);
    }

    [Theory]
    [MemberData(nameof(NotImages))]
    public async Task Anything_that_is_not_jpeg_png_or_webp_is_rejected(string description, byte[] bytes)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => Upload(bytes));

        Assert.True(error.Fields["file"] == UploadService.WrongTypeMessage, description);
        Assert.Empty(_storage.Files);
        Assert.Empty(_media.Added);
    }

    public static TheoryData<string, byte[]> NotImages() => new()
    {
        { "plain text renamed to jpg", Encoding.UTF8.GetBytes("this is not a picture at all") },
        { "svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "html", Encoding.UTF8.GetBytes("<html><body>hi</body></html>") },
        { "gif", [.. "GIF89a"u8, 1, 2, 3, 4, 5, 6] },
        { "pdf", [.. "%PDF-1.7"u8, 1, 2, 3] },
        { "riff but not webp", [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8, 1, 2] },
        { "png signature cut short", [0x89, 0x50, 0x4E, 0x47] },
        { "two bytes", [0xFF, 0xD8] },
    };

    // ---------------------------------------------------------------- size

    [Fact]
    public async Task An_empty_file_is_a_validation_error()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => Upload([]));

        Assert.Equal(UploadService.MissingFileMessage, error.Fields["file"]);
    }

    [Fact]
    public async Task A_file_of_exactly_five_megabytes_is_accepted()
    {
        await Upload(Jpeg(UploadService.MaxBytes));

        Assert.Single(_storage.Files);
        Assert.Equal(UploadService.MaxBytes, _media.Added.Single().SizeBytes);
    }

    [Fact]
    public async Task A_file_one_byte_over_the_limit_is_refused_and_nothing_is_stored()
    {
        await Assert.ThrowsAsync<PayloadTooLargeException>(() => Upload(Jpeg(UploadService.MaxBytes + 1)));

        Assert.Empty(_storage.Files);
        Assert.Empty(_media.Added);
    }

    [Fact]
    public async Task An_oversized_stream_is_not_read_to_the_end()
    {
        using var endless = new EndlessStream();

        await Assert.ThrowsAsync<PayloadTooLargeException>(() => _service.UploadAsync("big.jpg", endless));

        Assert.True(endless.BytesRead < UploadService.MaxBytes + 200_000);
    }

    // ---------------------------------------------------------------- names

    [Theory]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData("../../appsettings.json.png", "appsettings.json.png")]
    [InlineData("C:\\Users\\me\\Pictures\\beach.jpg", "beach.jpg")]
    [InlineData("a/b/c.png", "c.png")]
    [InlineData("bad\u0000name\r\n.jpg", "badname.jpg")]
    [InlineData("   ", "upload")]
    [InlineData("", "upload")]
    [InlineData(null, "upload")]
    [InlineData("folder/", "upload")]
    public void The_original_name_is_cleaned(string? name, string expected)
        => Assert.Equal(expected, UploadService.CleanFileName(name));

    [Fact]
    public void A_very_long_original_name_is_shortened_to_fit_the_record()
        => Assert.Equal(UploadService.MaxOriginalNameLength, UploadService.CleanFileName(new string('a', 500)).Length);

    [Fact]
    public async Task The_stored_name_never_uses_the_client_name_and_is_unique()
    {
        var first = await Upload(Jpeg(), "../../etc/passwd.jpg");
        var second = await Upload(Jpeg(), "../../etc/passwd.jpg");

        Assert.NotEqual(first.Url, second.Url);
        Assert.DoesNotContain("passwd", first.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("..", first.Url, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task The_file_is_removed_when_the_record_cannot_be_saved()
    {
        _media.FailOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload(Jpeg()));

        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task Nothing_is_recorded_when_the_file_cannot_be_stored()
    {
        _storage.FailOnSave = true;

        await Assert.ThrowsAsync<IOException>(() => Upload(Jpeg()));

        Assert.Empty(_media.Added);
    }

    // ---------------------------------------------------------------- fakes

    private sealed class FakeStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public bool FailOnSave { get; set; }

        public Task SaveAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        {
            if (FailOnSave)
            {
                throw new IOException("disk full");
            }

            Files.Add(fileName, content.ToArray());
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
        {
            Files.Remove(fileName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMedia : IRepository<MediaFile>, IUnitOfWork
    {
        public List<MediaFile> Added { get; } = [];

        public int Saves { get; private set; }

        public bool FailOnSave { get; set; }

        public Task<MediaFile?> GetByIdAsync(object id, CancellationToken cancellationToken = default) => Task.FromResult<MediaFile?>(null);

        public Task AddAsync(MediaFile entity, CancellationToken cancellationToken = default)
        {
            Added.Add(entity);
            return Task.CompletedTask;
        }

        public void Remove(MediaFile entity) => Added.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailOnSave)
            {
                Added.Clear();
                throw new InvalidOperationException("database down");
            }

            Saves++;
            return Task.FromResult(1);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    /// <summary>Starts like a JPEG and never ends.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Fill(buffer.Span));

        private int Fill(Span<byte> span)
        {
            span.Clear();
            if (BytesRead == 0 && span.Length >= 3)
            {
                span[0] = 0xFF;
                span[1] = 0xD8;
                span[2] = 0xFF;
            }

            BytesRead += span.Length;
            return span.Length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
