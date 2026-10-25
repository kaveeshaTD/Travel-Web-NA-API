using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration.Support;
using TravelWebAPI.Tests.Unit;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Uploading an image as admin and fetching it back, through the real API, database and a temporary uploads folder.</summary>
[Collection(PostgresCollection.Name)]
public sealed class UploadEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static readonly byte[] Jpeg = UploadServiceTests.Jpeg(2_000);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // The upload record points at the admin who made it, so that admin needs a row.
        await using var db = PostgresFixture.CreateContext(Connection);
        var admin = TestData.User("admin@example.com");
        admin.Id = AdminId;
        admin.Role = UserRole.Admin;
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }

    private string[] StoredFiles => Directory.GetFiles(Factory.UploadsPath).Select(Path.GetFileName).Order().ToArray()!;

    private Task<HttpResponseMessage> UploadAsync(
        byte[] bytes,
        string fileName = "photo.jpg",
        string contentType = "image/jpeg",
        string? token = "admin",
        string field = "file")
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(part, field, fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads") { Content = form };
        var bearer = token == "admin" ? AdminToken : token == "customer" ? CustomerToken : token;
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return Api.SendAsync(request);
    }

    // ---------------------------------------------------------------- upload

    [Fact]
    public async Task An_admin_upload_is_201_with_the_public_path_and_a_media_record()
    {
        var response = await UploadAsync(Jpeg, "Holiday Photo.JPG");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var url = (await Json(response)).GetProperty("url").GetString()!;
        Assert.Matches("^/uploads/[0-9a-f]{32}\\.jpg$", url);
        Assert.Equal(url, response.Headers.Location?.ToString());
        Assert.Equal(url, "/uploads/" + Assert.Single(StoredFiles));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM media_files"));
        Assert.Equal("Holiday Photo.JPG", await Scalar<string>("SELECT original_name FROM media_files"));
        Assert.Equal("image/jpeg", await Scalar<string>("SELECT content_type FROM media_files"));
        Assert.Equal(Jpeg.Length, await Scalar<int>("SELECT size_bytes FROM media_files"));
        Assert.Equal(AdminId.ToString(), await Scalar<string>("SELECT uploaded_by::text FROM media_files"));
        Assert.Equal(url[9..], await Scalar<string>("SELECT file_name FROM media_files"));
    }

    [Fact]
    public async Task The_uploaded_file_is_served_to_anyone_with_safe_headers()
    {
        var upload = await UploadAsync(Jpeg);
        var url = (await Json(upload)).GetProperty("url").GetString()!;

        var response = await Api.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Public);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
        Assert.Contains("immutable", response.Headers.GetValues("Cache-Control").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task Png_and_webp_files_keep_their_own_type(string type, string contentType)
    {
        var bytes = type == "png" ? UploadServiceTests.Png() : UploadServiceTests.Webp();

        var upload = await UploadAsync(bytes, $"x.{type}", contentType);
        var url = (await Json(upload)).GetProperty("url").GetString()!;
        var served = await Api.GetAsync(url);

        Assert.EndsWith($".{type}", url, StringComparison.Ordinal);
        Assert.Equal(contentType, served.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_content_decides_the_type_not_the_declared_type_or_file_name()
    {
        var upload = await UploadAsync(UploadServiceTests.Png(), "page.html", "text/html");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var url = (await Json(upload)).GetProperty("url").GetString()!;
        Assert.EndsWith(".png", url, StringComparison.Ordinal);
        Assert.Equal("image/png", await Scalar<string>("SELECT content_type FROM media_files"));
    }

    [Fact]
    public async Task Two_uploads_of_the_same_file_get_different_names()
    {
        var first = await Json(await UploadAsync(Jpeg));
        var second = await Json(await UploadAsync(Jpeg));

        Assert.NotEqual(first.GetProperty("url").GetString(), second.GetProperty("url").GetString());
        Assert.Equal(2, StoredFiles.Length);
    }

    [Fact]
    public async Task A_hostile_client_name_never_reaches_the_disk_path()
    {
        var response = await UploadAsync(UploadServiceTests.Png(), "../../appsettings.json.png");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single(StoredFiles);
        Assert.Matches("^[0-9a-f]{32}\\.png$", stored);
        Assert.Equal("appsettings.json.png", await Scalar<string>("SELECT original_name FROM media_files"));
        Assert.False(File.Exists(Path.Combine(Factory.UploadsPath, "..", "appsettings.json.png")));
    }

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task Only_an_admin_can_upload()
    {
        var anonymous = await UploadAsync(Jpeg, token: null);
        var customer = await UploadAsync(Jpeg, token: "customer");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.Empty(StoredFiles);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM media_files"));
    }

    // ---------------------------------------------------------------- bad input

    [Fact]
    public async Task A_request_without_a_file_is_400()
    {
        var wrongField = await UploadAsync(Jpeg, field: "photo");
        var emptyFile = await UploadAsync([]);

        foreach (var response in new[] { wrongField, emptyFile })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Choose an image to upload", await Field(response, "file"));
        }

        Assert.Empty(StoredFiles);
    }

    [Fact]
    public async Task A_json_body_is_415()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/uploads", new { filename = "a.png", dataUrl = "data:image/png;base64,AAAA" });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        AuthApi.AssertError(await Json(response), HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("a text file named photo.jpg")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    public async Task Files_that_are_not_jpeg_png_or_webp_are_400_and_not_stored(string content)
    {
        var response = await UploadAsync(Encoding.UTF8.GetBytes(content), "photo.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Only JPEG, PNG or WebP images are allowed", await Field(response, "file"));
        Assert.Empty(StoredFiles);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM media_files"));
    }

    // ---------------------------------------------------------------- size

    [Fact]
    public async Task A_five_megabyte_image_is_accepted()
    {
        var response = await UploadAsync(UploadServiceTests.Jpeg(5 * 1024 * 1024));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(5 * 1024 * 1024, await Scalar<int>("SELECT size_bytes FROM media_files"));
    }

    [Fact]
    public async Task A_six_megabyte_image_is_413_in_the_uniform_shape_and_not_stored()
    {
        var response = await UploadAsync(UploadServiceTests.Jpeg(6 * 1024 * 1024));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        AuthApi.AssertError(await Json(response), HttpStatusCode.RequestEntityTooLarge);
        Assert.Empty(StoredFiles);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM media_files"));
    }

    [Fact]
    public async Task A_file_just_over_five_megabytes_inside_the_request_limit_is_413()
    {
        var response = await UploadAsync(UploadServiceTests.Jpeg((5 * 1024 * 1024) + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(StoredFiles);
    }

    // ---------------------------------------------------------------- serving

    [Fact]
    public async Task A_missing_upload_is_404_in_the_uniform_shape()
    {
        var response = await Api.GetAsync("/uploads/unknown.jpg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AuthApi.AssertError(await Json(response), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("/uploads/%2e%2e/appsettings.json")]
    [InlineData("/uploads/%2e%2e%2fappsettings.json")]
    [InlineData("/uploads/..%5cappsettings.json")]
    public async Task Paths_that_climb_out_of_the_uploads_folder_do_not_return_files(string path)
    {
        var response = await Api.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("AllowedHosts", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_three_image_types_are_served_from_the_folder()
    {
        await File.WriteAllTextAsync(Path.Combine(Factory.UploadsPath, "notes.txt"), "secret");
        await File.WriteAllTextAsync(Path.Combine(Factory.UploadsPath, "page.html"), "<b>x</b>");

        var text = await Api.GetAsync("/uploads/notes.txt");
        var html = await Api.GetAsync("/uploads/page.html");

        Assert.Equal(HttpStatusCode.NotFound, text.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, html.StatusCode);
    }

    // ---------------------------------------------------------------- with content

    [Fact]
    public async Task An_upload_can_be_used_in_content_and_deleting_the_content_keeps_the_file()
    {
        var upload = await UploadAsync(Jpeg);
        var url = (await Json(upload)).GetProperty("url").GetString()!;

        var image = await CreateAsync("gallery", Gallery("Uploaded photo", new { src = url }));
        var deleted = await AsAdmin(HttpMethod.Delete, $"/api/gallery/{image.GetProperty("id").GetString()}");
        var served = await Api.GetAsync(url);

        Assert.Equal(url, image.GetProperty("src").GetString());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM media_files"));
        Assert.Single(StoredFiles);
    }

    // ---------------------------------------------------------------- start-up

    [Fact]
    public void A_storage_path_that_cannot_be_used_stops_the_start_up()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"ceylon-roads-not-a-folder-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "this is a file, not a folder");

        try
        {
            using var factory = ApiFactory.With(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = Connection,
                ["Storage:UploadsPath"] = Path.Combine(blocker, "uploads"),
            });

            Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
