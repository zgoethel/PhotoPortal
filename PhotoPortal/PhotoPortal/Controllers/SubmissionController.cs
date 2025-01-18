using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using PhotoPortal.Shared;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PhotoPortal.Controllers
{
    public class SubmissionController(
        IConfiguration config
        ) : Controller
    {
        private readonly JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true
        };

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

            var sanitizedFrom = Regex.Replace(dto.From, "[^a-zA-Z0-9]+", "_");
            var metaPath = Path.Combine(basePath, $"{DateTime.Now:yyyyMMddHHmmss}_{sanitizedFrom}.txt");

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

                var fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{sanitizedFrom}_{i}_{sanitizedName}.{extension}";
                var filePath = Path.Combine(basePath, fileName);

                var contents = Convert.FromBase64String(file.Base64);
                await System.IO.File.WriteAllBytesAsync(filePath, contents);

                try
                {
                    // http://example.com/owncloud/remote.php/webdav/

                    using var http = new HttpClient();
                    http.BaseAddress = new(config["OwnCloud"]);

                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(config["OwnCloudCreds"]));
                    http.DefaultRequestHeaders.Authorization = new("Basic", credentials);

                    var knownType = new FileExtensionContentTypeProvider().TryGetContentType(file.OriginalName, out var contentType);
                    if (!knownType)
                    {
                        contentType = "application/octet-stream";
                    }

                    using var multipartContent = new MultipartFormDataContent();
                    using var content = new ByteArrayContent(contents);
                    content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    multipartContent.Add(content, "myfile", file.OriginalName);

                    var putPath = $"./remote.php/webdav/{config["OwnCloudPath"].Trim('/', '\\')}/{fileName}";
                    var result = await http.PutAsync(putPath, content);

                    result.EnsureSuccessStatusCode();
                } catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            return Ok();
        }
    }
}
