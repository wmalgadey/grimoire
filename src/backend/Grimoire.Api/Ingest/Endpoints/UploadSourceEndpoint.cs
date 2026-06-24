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
            var rootDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "raw", "sources"));

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
            if (!TryNormalizeSubDirectory(subDirectory, out var safeSubDirectory))
            {
                return Results.BadRequest(new
                {
                    error = "InvalidSubDirectory",
                    message = "subDirectory must be a relative path under raw/sources without traversal segments."
                });
            }

            var baseDir = Path.Combine(rootDir, safeSubDirectory);
            var normalizedBaseDir = Path.GetFullPath(baseDir);

            if (!IsUnderRoot(normalizedBaseDir, rootDir))
            {
                return Results.BadRequest(new
                {
                    error = "InvalidSubDirectory",
                    message = "subDirectory must resolve within raw/sources."
                });
            }

            Directory.CreateDirectory(normalizedBaseDir);

            foreach (var file in files)
            {
                var safeFileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(safeFileName))
                {
                    rejected.Add(new
                    {
                        fileName = file.FileName,
                        error = "InvalidFileName",
                        message = "File name must not be empty."
                    });
                    continue;
                }

                if (file.Length > MaxFileSize)
                {
                    rejected.Add(new
                    {
                        fileName = safeFileName,
                        error = "FileTooLarge",
                        message = $"File '{safeFileName}' exceeds the 100 MB upload limit."
                    });
                    continue;
                }

                try
                {
                    var destination = Path.GetFullPath(Path.Combine(normalizedBaseDir, safeFileName));
                    if (!IsUnderRoot(destination, rootDir))
                    {
                        rejected.Add(new
                        {
                            fileName = safeFileName,
                            error = "InvalidFilePath",
                            message = "File path must resolve within raw/sources."
                        });
                        continue;
                    }

                    using (var stream = System.IO.File.Create(destination))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var relativePath = Path.Combine("raw", "sources", safeSubDirectory, safeFileName)
                        .Replace("\\", "/");
                    accepted.Add(new
                    {
                        fileName = safeFileName,
                        destination = relativePath
                    });
                }
                catch (Exception ex)
                {
                    rejected.Add(new
                    {
                        fileName = safeFileName,
                        error = "UploadFailed",
                        message = ex.Message
                    });
                }
            }

            return Results.Accepted("/api/ingest/upload", new { accepted, rejected });
        });

        return routes;
    }

    private static bool TryNormalizeSubDirectory(string? subDirectory, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(subDirectory))
        {
            return true;
        }

        var candidate = subDirectory.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(candidate))
        {
            return false;
        }

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment == "." || segment == ".."))
        {
            return false;
        }

        normalized = Path.Combine(segments);
        return true;
    }

    private static bool IsUnderRoot(string candidatePath, string rootPath)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(normalizedCandidate, normalizedRoot, StringComparison.Ordinal);
    }
}
