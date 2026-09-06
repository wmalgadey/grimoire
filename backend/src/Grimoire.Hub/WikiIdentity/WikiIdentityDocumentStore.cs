using System.Security.Cryptography;

namespace Grimoire.Hub.WikiIdentity;

/// <summary>
/// Terminal outcome of a persist attempt (029-shared-foundation-prompt T034, ADR-053's
/// authorship rule, contracts/wiki-identity-cli.md): the vocabulary the
/// <c>wiki.identity.wizard_outcomes_total</c> metric's <c>outcome</c> label and the
/// wizard's own exit code both derive from.
/// </summary>
public enum WikiIdentityPersistOutcome
{
    /// <summary>The bytes were written verbatim to the instance document path.</summary>
    Persisted,

    /// <summary>The content was empty/whitespace-only (data-model.md §1 "Validity") — rejected, nothing placed.</summary>
    Rejected,

    /// <summary>An instance document already exists and no explicit replace decision was given (FR-014).</summary>
    ReplaceRefused,
}

/// <summary>One persist attempt's full result — everything the command needs to report the outcome and log it.</summary>
public sealed record WikiIdentityPersistResult(
    WikiIdentityPersistOutcome Outcome, string? Sha256, int Bytes, bool ReplacedExisting, string? RejectionReason);

/// <summary>
/// Reads and writes the instance foundation document at a fixed path
/// (<c>&lt;DataDir&gt;/foundation-prompt.md</c>, ADR-053/FR-002). This is the one type this
/// feature's ADR-053 authorship rule allows to write that filename outside the resolver
/// (Phase 0's allow-list entry) — and it does so byte-for-byte: it never composes,
/// templates, or otherwise transforms what it is handed (FR-013a). Validation is limited to
/// custody — readable and not effectively empty — never to what the content says.
/// </summary>
public sealed class WikiIdentityDocumentStore(string instancePath)
{
    /// <summary>Whether an instance document currently exists at this store's path.</summary>
    public bool Exists => File.Exists(instancePath);

    /// <summary>
    /// Persists <paramref name="content"/> verbatim, subject to the replace guard (FR-014)
    /// and the effectively-empty check (data-model.md §1). Re-checks existence at the point
    /// of writing rather than trusting a cached flag, since the wizard is the only writer
    /// but a hand-edited/hand-deleted file must still be observed correctly.
    /// </summary>
    public async Task<WikiIdentityPersistResult> PersistAsync(byte[] content, bool replace, CancellationToken cancellationToken)
    {
        if (IsEffectivelyEmpty(content))
        {
            return new WikiIdentityPersistResult(WikiIdentityPersistOutcome.Rejected, null, 0, false,
                "the drafted document is empty or whitespace-only");
        }

        var existed = Exists;
        if (existed && !replace)
        {
            var existingSha256 = ComputeSha256(await File.ReadAllBytesAsync(instancePath, cancellationToken));
            return new WikiIdentityPersistResult(WikiIdentityPersistOutcome.ReplaceRefused, existingSha256, 0, false,
                "an instance document already exists and --replace was not given");
        }

        await File.WriteAllBytesAsync(instancePath, content, cancellationToken);
        return new WikiIdentityPersistResult(WikiIdentityPersistOutcome.Persisted, ComputeSha256(content), content.Length, existed, null);
    }

    private static bool IsEffectivelyEmpty(byte[] content)
        => content.Length == 0 || string.IsNullOrWhiteSpace(System.Text.Encoding.UTF8.GetString(content));

    private static string ComputeSha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
