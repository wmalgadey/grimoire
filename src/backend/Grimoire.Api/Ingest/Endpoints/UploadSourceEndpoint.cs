namespace Grimoire.Api.Ingest.Endpoints;

public static class UploadSourceEndpoint
{
    private const long MaxFileSize = 100 * 1024 * 1024;

    public static IEndpointRouteBuilder MapUploadSource(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ingest/upload", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var files = form.Files;
            var subDirectory = form["subDirectory"].ToString();

            if (files.Count == 0)
            {
                return Results.BadRequest(new
                {
                    error = "NoFilesProvided",
                    message = "At least one file must be provided."
                });
            }

            var accepted = new List<object>();
            var rejected = new List<object>();
            var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "raw", "sources", subDirectory ?? "");

            Directory.CreateDirectory(baseDir);

            foreach (var file in files)
            {
                if (file.Length > MaxFileSize)
                {
                    rejected.Add(new
                    {
                        fileName = file.FileName,
                        error = "FileTooLarge",
                        message = $"File '{file.FileName}' exceeds the 100 MB upload limit."
                    });
                    continue;
                }

                try
                {
                    var destination = Path.Combine(baseDir, file.FileName);
                    using (var stream = System.IO.File.Create(destination))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var relativePath = Path.Combine("raw", "sources", subDirectory ?? "", file.FileName)
                        .Replace("\\", "/");
                    accepted.Add(new
                    {
                        fileName = file.FileName,
                        destination = relativePath
                    });
                }
                catch (Exception ex)
                {
                    rejected.Add(new
                    {
                        fileName = file.FileName,
                        error = "UploadFailed",
                        message = ex.Message
                    });
                }
            }

            return Results.Accepted("/api/ingest/upload", new { accepted, rejected });
        });

        return routes;
    }
}
