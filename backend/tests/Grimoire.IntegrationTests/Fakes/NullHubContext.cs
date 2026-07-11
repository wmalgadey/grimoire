using Grimoire.Hub.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>No-op <see cref="IHubContext{IngestLifecycleHub}"/> for tests that only need to observe logs/spans/metrics, not real message delivery.</summary>
public sealed class NullHubContext : IHubContext<IngestLifecycleHub>
{
    public IHubClients Clients { get; } = new NullClients();
    public IGroupManager Groups => throw new NotSupportedException();

    private sealed class NullClients : IHubClients
    {
        public IClientProxy All { get; } = new NullClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Client(string connectionId) => All;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
        public IClientProxy Group(string groupName) => All;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
        public IClientProxy User(string userId) => All;
        public IClientProxy Users(IReadOnlyList<string> userIds) => All;
    }

    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
