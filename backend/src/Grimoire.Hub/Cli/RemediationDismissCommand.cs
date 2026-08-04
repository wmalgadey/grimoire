using Grimoire.Hub.RemediationTasks;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Dismisses a proposed remediation task via
/// <see cref="RemediationTaskTransitionService.DismissAsync"/> — the same in-process call
/// the HTTP <c>/dismiss</c> endpoint now makes (018-hub-cli-commands T024,
/// contracts/cli-commands.md "remediation-dismiss", FR-005/SC-005). No agent work is ever
/// involved (FR-010) — the transition completes immediately, so unlike
/// <see cref="RemediationAuthorizeCommand"/> there is nothing to supervise.
/// </summary>
public sealed class RemediationDismissCommand : AsyncCommand<RemediationTaskSettings>
{
    private readonly RemediationTaskTransitionService _service;
    private readonly TextWriter _stdout;

    // See LintRunCommand's identical attribute for why this is required (018-hub-cli-commands
    // T036 quickstart validation finding): disambiguates ActivatorUtilities.CreateInstance
    // between this constructor and the test seam below.
    [ActivatorUtilitiesConstructor]
    public RemediationDismissCommand(RemediationTaskTransitionService service)
        : this(service, Console.Out)
    {
    }

    /// <summary>Test seam: inject a stdout writer instead of the real stdout stream.</summary>
    public RemediationDismissCommand(RemediationTaskTransitionService service, TextWriter stdout)
    {
        _service = service;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, RemediationTaskSettings settings, CancellationToken cancellationToken)
    {
        var taskId = settings.TaskId!;
        var result = await _service.DismissAsync(taskId, cancellationToken);

        switch (result)
        {
            case RemediationTransitionResult.NotFound:
                _stdout.WriteLine($"Remediation task '{taskId}' was not found.");
                return (int)CliExitCode.NotFound;

            case RemediationTransitionResult.Conflict conflict:
                _stdout.WriteLine($"Remediation task {taskId} is not proposed (current state: {conflict.CurrentState}).");
                return (int)CliExitCode.StateConflict;

            case RemediationTransitionResult.Ok ok:
                _stdout.WriteLine($"Remediation task {ok.TaskId} dismissed.");
                return (int)CliExitCode.Success;

            default:
                throw new InvalidOperationException($"Unhandled {nameof(RemediationTransitionResult)}: {result.GetType()}.");
        }
    }
}
