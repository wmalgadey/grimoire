using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grimoire.Ingest.Git;

public class IngestGitService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestGitService> _logger;

    public IngestGitService(IConfiguration configuration, ILogger<IngestGitService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string> CommitAsync(IEnumerable<string> filePaths, int fileCount, int chunkCount)
    {
        var repoPath = _configuration["Git:RepoPath"]
            ?? _configuration["INGEST_GIT_REPO_PATH"]
            ?? ".";
        var userName = _configuration["Git:UserName"]
            ?? _configuration["INGEST_GIT_AUTHOR_NAME"]
            ?? "Grimoire Ingest";
        var userEmail = _configuration["Git:UserEmail"]
            ?? _configuration["INGEST_GIT_AUTHOR_EMAIL"]
            ?? "ingest@grimoire";
        var now = DateTimeOffset.UtcNow;
        var message = $"ingest: {fileCount} file(s), {chunkCount} chunks — {now:o}";

        try
        {
            using var repo = new Repository(repoPath);

            var fileList = filePaths.ToList();
            foreach (var path in fileList)
            {
                Commands.Stage(repo, path);
            }

            var status = repo.RetrieveStatus();
            if (!status.IsDirty)
            {
                _logger.LogInformation("ingest.git_commit skipped — nothing to commit");
                return Task.FromResult(string.Empty);
            }

            var author = new Signature(userName, userEmail, now);
            var commit = repo.Commit(message, author, author);

            _logger.LogInformation(
                "ingest.git_commit commit_sha={CommitSha} file_count={FileCount} chunk_count={ChunkCount}",
                commit.Sha, fileCount, chunkCount);

            return Task.FromResult(commit.Sha);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ingest.git_commit failed file_count={FileCount}", fileCount);
            return Task.FromResult(string.Empty);
        }
    }
}
