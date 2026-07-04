namespace Grimoire.IngestAgent.Guardrails;

public sealed class GuardedFileOperations
{
    private readonly string _repoRoot;
    private readonly GuardrailEvaluator _evaluator;

    public GuardedFileOperations(string repoRoot, GuardrailEvaluator evaluator)
    {
        _repoRoot = Path.GetFullPath(repoRoot);
        _evaluator = evaluator;
    }

    public IReadOnlyList<DeniedAction> DeniedActions => _deniedActions;

    private readonly List<DeniedAction> _deniedActions = [];

    public async Task<string?> ReadAllTextAsync(string fullPath, CancellationToken cancellationToken)
    {
        var relative = ToRepoRelativePath(fullPath);
        var decision = _evaluator.Evaluate(GuardrailAction.Read, relative);
        if (!decision.IsAllowed)
        {
            _deniedActions.Add(new DeniedAction("read", relative, decision.Reason, decision.RuleId));
            return null;
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public async Task<bool> WriteAllTextAsync(string fullPath, string content, CancellationToken cancellationToken)
    {
        var relative = ToRepoRelativePath(fullPath);
        var decision = _evaluator.Evaluate(GuardrailAction.Write, relative);
        if (!decision.IsAllowed)
        {
            _deniedActions.Add(new DeniedAction("write", relative, decision.Reason, decision.RuleId));
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? _repoRoot);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return true;
    }

    private string ToRepoRelativePath(string fullPath)
    {
        var normalizedFullPath = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(_repoRoot, normalizedFullPath).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
        {
            throw new InvalidOperationException("Path is outside repository root.");
        }

        return relative;
    }
}

public sealed record DeniedAction(string Action, string TargetPath, string Reason, string? RuleId);
