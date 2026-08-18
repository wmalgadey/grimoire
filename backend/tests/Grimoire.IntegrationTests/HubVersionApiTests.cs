using System.Net;
using System.Reflection;
using System.Text.Json;
using Grimoire.Hub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// <c>GET /api/version</c> answers with the version of the Hub that served the request, so the
/// frontend's connection indicator can name the server behind a live connection instead of only
/// asserting that one exists.
///
/// <para>
/// What is asserted is our own contract (Constitution Principle II, "Test what we own"): the
/// route we mapped, the field name the frontend reads, and that the value tracks the running
/// assembly's stamped version rather than a literal written into the endpoint. The version's
/// derivation from git history is GitVersion's job (ADR-027) and is not re-verified here — the
/// expected value below is read from the assembly the endpoint itself is compiled into, so a
/// build with no repository (<c>0.0.0-nogit</c>) is as valid a subject as a tagged release.
/// </para>
///
/// <para>
/// Hosted against a real request pipeline through the production
/// <see cref="HubVersionEndpoints.MapHubVersionEndpoints"/>, with no Grimoire services
/// registered at all — which is itself part of the contract: the endpoint an operator reaches
/// for when something is wrong must not depend on the parts that might be wrong.
/// </para>
/// </summary>
public sealed class HubVersionApiTests
{
    [Fact]
    public async Task Version_NamesTheRunningHubBuild()
    {
        using var host = await BuildHostAsync();

        var response = await host.GetTestClient().GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var version = ReadVersionField(await response.Content.ReadAsStringAsync());
        Assert.Equal(StampedVersionWithoutBuildMetadata(), version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public async Task Version_DropsTheBuildMetadata_SoItNamesAReleaseNotABuild()
    {
        // GitVersion appends `+<height>.Branch.<name>.Sha.<sha>` to the informational version.
        // An operator reading the connection indicator wants the release; the sha would make the
        // panel unreadable and is available from the deployment record either way.
        using var host = await BuildHostAsync();

        var response = await host.GetTestClient().GetAsync("/api/version");

        var version = ReadVersionField(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain('+', version);
    }

    private static string ReadVersionField(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("version").GetString() ?? string.Empty;
    }

    /// <summary>
    /// The version the SDK stamped onto the Hub assembly, read independently of
    /// <see cref="HubVersion"/> so this asserts the endpoint reports the running build rather
    /// than merely re-stating whatever the production helper returned.
    /// </summary>
    private static string StampedVersionWithoutBuildMetadata()
    {
        var assembly = typeof(HubVersionEndpoints).Assembly;
        var stamped = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? string.Empty;
        return stamped.Split('+')[0];
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapGroup("/api/version").MapHubVersionEndpoints();

        await app.StartAsync();
        return app;
    }
}
