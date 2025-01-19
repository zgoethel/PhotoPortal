using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using PhotoPortal.Services;
using PhotoPortal.Shared;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace PhotoPortal.Controllers;

public class SubmissionController(
    IConfiguration config,
    EmailSender email
    ) : Controller
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };

    [AllowAnonymous]
    [HttpGet("/i/{token}/{fileName}")]
    [ResponseCache(Duration = 1800)]
    public IActionResult Image(string token, string fileName, string originalName)
    {
        if (token != config["Token"])
        {
            return Forbid();
        }

        var basePath = config["FileStorage"];
        var fullPath = Path.GetFullPath(Path.Combine(basePath, Path.GetFileName(fileName)));

        var knownType = new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var contentType);
        if (!knownType)
        {
            contentType = "application/octet-stream";
        }

        if (string.IsNullOrEmpty(originalName))
        {
            originalName = Path.GetFileName(fileName);
        }
        return PhysicalFile(fullPath, contentType, originalName);
    }

    [AllowAnonymous]
    [HttpPost("/Submit")]
    [RequestSizeLimit(1_000_000_000)]
    public async Task<IActionResult> Submit([FromBody] Submission.WithFiles dto, [FromQuery] string token)
    {
        if (token != config["Token"])
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(dto.From)
            || dto.From.Length > 150
            || dto.Message.Length > 10000
            || dto.NumAttachments != dto.FileContents.Count
            || dto.NumAttachments > 5)
        {
            return BadRequest();
        }

        dto.Submitted = DateTime.UtcNow;

        var basePath = config["FileStorage"];
        Directory.CreateDirectory(basePath);

        var storedFileNames = new List<(string original, string stored)>();

        var sanitizedFrom = Regex.Replace(dto.From, "[^a-zA-Z0-9]+", "_");
        var metaPath = Path.Combine(basePath, $"{dto.Submitted:yyyyMMddHHmmss}_{sanitizedFrom}.txt");

        var meta = JsonSerializer.Serialize<Submission>(dto, jsonOptions);
        await System.IO.File.WriteAllTextAsync(metaPath, meta);

        foreach (var (file, i) in dto.FileContents.Select((it, i) => (it, i)))
        {
            var name = Path.GetFileNameWithoutExtension(file.OriginalName);
            var sanitizedName = Regex.Replace(name, "[^a-zA-Z0-9]+", "_");
            if (sanitizedName.Length > 10)
            {
                sanitizedName = sanitizedName[..10];
            }

            var extension = Path.GetExtension(file.OriginalName);

            var fileName = $"{dto.Submitted:yyyyMMddHHmmss}_{sanitizedFrom}_{i}_{sanitizedName}{extension}";
            var filePath = Path.Combine(basePath, fileName);

            var contents = Convert.FromBase64String(file.Base64);
            await System.IO.File.WriteAllBytesAsync(filePath, contents);

            storedFileNames.Add((file.OriginalName, fileName));

            try
            {
                await UploadToOwnCloud(fileName, contents);
            } catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        if (dto.EmailMe)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await EmailResponses(dto, $"{Request.Scheme}://{Request.Host}/{Request.PathBase.Value?.TrimStart('/')}".TrimEnd('/') + $"/i/{token}", storedFileNames);
                } catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            });
        }

        return Ok();
    }

    private async Task UploadToOwnCloud(string fileName, byte[] contents)
    {
        using var http = new HttpClient();
        http.BaseAddress = new(config["OwnCloud"]);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(config["OwnCloudCreds"]));
        http.DefaultRequestHeaders.Authorization = new("Basic", credentials);

        var knownType = new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var contentType);
        if (!knownType)
        {
            contentType = "application/octet-stream";
        }

        using var content = new ByteArrayContent(contents);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var putPath = $"./remote.php/webdav/{config["OwnCloudPath"].Trim('/', '\\')}/{fileName}";
        var result = await http.PutAsync(putPath, content);

        result.EnsureSuccessStatusCode();
    }

    private async Task EmailResponses(Submission.WithFiles dto, string basePath, List<(string original, string stored)> storedFileNames)
    {
        await email.SendEmailAsync(
            dto.EmailAddress,
            "Your photos. Thanks for coming!",
            $@"
            <div>
                <p><strong>Name:</strong> {HttpUtility.HtmlEncode(dto.From)}
                <p>
                    <strong>Message:</strong><br />
                    {HttpUtility.HtmlEncode(dto.Message).Replace("\n", "<br />")}
                </p>
                {(storedFileNames.Count > 0 ? $"<p>Your {(storedFileNames.Count > 1 ? $"{storedFileNames.Count} photos are" : "photo is")} below!</p>" : "")}
                <p>You can view everybody's photos and upload more: <a href='{config["Gallery"]}'>View Gallery</a></p>
                <p>&mdash;<br />The Solies</p>

                <p>{string.Join("</p><p>", storedFileNames
                        .Select((it) =>
                        {
                            var imagePath = $"{basePath.TrimEnd('/')}/{it.stored}?originalName={HttpUtility.UrlEncode(it.original)}";
                            return
                                $@"
                                <img src='{imagePath}' style='max-width: 500px;' />
                                <p><a href='{imagePath}'>Download</a><br /></p>
                                ";
                        })
                    )}</p>
            </div>
            ");
    }

    [Authorize]
    [HttpGet("/Submission/ListResponses")]
    public async Task<IActionResult> ListResponses(bool? read)
    {
        var basePath = config["FileStorage"];
        var submissionFiles = Directory.EnumerateFiles(basePath, $"*.txt", SearchOption.TopDirectoryOnly);

        var responses = new List<Submission.AdminView>();

        foreach (var file in submissionFiles)
        {
            var fileBase = Path.GetFileNameWithoutExtension(file);
            var isRead = System.IO.File.Exists($"{fileBase}.read");
            if (read.HasValue && read.Value != isRead)
            {
                continue;
            }

            var metaJson = await System.IO.File.ReadAllTextAsync(file);
            var meta = JsonSerializer.Deserialize<Submission.AdminView>(metaJson);

            meta.FileBase = fileBase;
            meta.Read = isRead ? new FileInfo($"{fileBase}.read").CreationTimeUtc : null;
            meta.ImagePaths = [];

            var imageFiles = Directory.EnumerateFiles(basePath, $"{fileBase}_*_*.*");
            foreach (var image in imageFiles)
            {
                meta.ImagePaths.Add($"i/{config["Token"]}/{Path.GetFileName(image)}");
            }

            responses.Add(meta);
        }

        return Json(responses.OrderByDescending((it) => it.Submitted));
    }

    [Authorize]
    [HttpPost("/Submission/MarkRead")]
    public IActionResult MarkRead(string fileBase)
    {
        var basePath = config["FileStorage"];
        var fullPath = Path.Combine(basePath, $"{fileBase}.txt");

        if (System.IO.File.Exists(fullPath))
        {
            var readPath = Path.Combine(basePath, $"{fileBase}.read");
            if (!System.IO.File.Exists(readPath))
            {
                try
                {
                    System.IO.File.WriteAllText(readPath, "");
                } catch (Exception)
                {
                }
            }
        }

        return Ok();
    }
}
