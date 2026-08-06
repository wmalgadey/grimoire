using Grimoire.Hub.RemediationTasks;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Withdraws a remediation task's authorization (returns it to <c>proposed</c>) via
/// <see cref="RemediationTaskTransitionService.WithdrawAuthorizationAsync"/> — the same
/// in-process call the HTTP <c>/withdraw-authorization</c> endpoint now makes
/// (018-hub-cli-commands T025, contracts/cli-commands.md "remediation-withdraw",
/// FR-005/SC-005). No agent work is ever involved — the transition completes
/// immediately, so unlike <see cref="RemediationAuthorizeCommand"/> there is nothing to
/// supervise. Distinguishes the two conflict shapes
/// <see cref="RemediationTaskTransitionService.WithdrawAuthorizationAsync"/> can return
/// (including the lost-race case, where the coordinator's own CAS to <c>executing</c>
/// already won): <c>execution_already_started</c> vs. <c>task_not_authorized</c>.
/// </summary>
public sealed class RemediationWithdrawCommand : AsyncCommand<RemediationTaskSettings>
{
    private readonly RemediationTaskTransitionService _service;
    private readonly TextWriter _stdout;

    // See LintRunCommand's identical attribute for why this is required (018-hub-cli-commands
    // T036 quickstart validation finding): disambiguates ActivatorUtilities.CreateInstance
    // between this constructor and the test seam below.
    [ActivatorUtilitiesConstructor]
    public RemediationWithdrawCommand(RemediationTaskTransitionService service)
        : this(service, Console.Out)
    {
    }

    /// <summary>Test seam: inject a stdout writer instead of the real stdout stream.</summary>
    public RemediationWithdrawCommand(RemediationTaskTransitionService service, TextWriter stdout)
    {
        _service = service;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, RemediationTaskSettings settings, CancellationToken cancellationToken)
    {
        var taskId = settings.TaskId!;
        var result = await _service.WithdrawAuthorizationAsync(taskId, cancellationToken);

        switch (result)
        {
            case RemediationTransitionResult.NotFound:
                _stdout.WriteLine($"Remediation task '{taskId}' was not found.");
                return (int)CliExitCode.NotFound;

            case RemediationTransitionResult.Conflict { Reason: "execution_already_started" } conflict:
                _stdout.WriteLine(
                    $"Remediation task {taskId} can no longer be withdrawn: execution already started (state: {conflict.CurrentState}).");
                return (int)CliExitCode.StateConflict;

            case RemediationTransitionResult.Conflict conflict:
                _stdout.WriteLine($"Remediation task {taskId} is not authorized (current state: {conflict.CurrentState}).");
                return (int)CliExitCode.StateConflict;

            case RemediationTransitionResult.Ok ok:
                _stdout.WriteLine($"Remediation task {ok.TaskId} authorization withdrawn (state: proposed).");
                return (int)CliExitCode.Success;

            default:
                throw new InvalidOperationException($"Unhandled {nameof(RemediationTransitionResult)}: {result.GetType()}.");
        }
    }
}
