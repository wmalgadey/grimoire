using System.Net;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.TestSupport;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #120 — the adapter used to translate every provider rejection into the same terminal
/// failure: a rate limit and a transient provider fault discarded a run's work exactly as
/// permanently as a malformed request did. The status code was on the exception all along;
/// nothing read it.
/// <para>
/// The classification is the adapter's own contract, so it is exercised through real HTTP
/// against <see cref="FakeAnthropicEndpoint"/> — the SDK's status handling and exception
/// typing run for real, and what is asserted is Grimoire's translation of them (Principle
/// II, "Test what we own"). Both call paths are covered: Ingest and Lint take the
/// non-streaming one, Query the streaming one, and the two have separate catch sites.
/// </para>
/// </summary>
public class ModelProviderErrorClassificationTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "overloaded_error")]
    [InlineData(HttpStatusCode.InternalServerError, "api_error")]
    public async Task AConditionThatMayPass_IsClassifiedRetryable(HttpStatusCode status, string errorType)
    {
        var exception = await RejectionFromAsync(status, errorType, "The service is busy.");

        Assert.True(exception.IsRetryable);
        Assert.Equal((int)status, exception.StatusCode);
        Assert.Contains("retryable", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("terminal", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error")]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error")]
    [InlineData(HttpStatusCode.Forbidden, "permission_error")]
    [InlineData(HttpStatusCode.NotFound, "not_found_error")]
    public async Task ARequestTheProviderWillNeverAccept_IsClassifiedTerminal(
        HttpStatusCode status, string errorType)
    {
        var exception = await RejectionFromAsync(status, errorType, "The request is malformed.");

        Assert.False(exception.IsRetryable);
        Assert.Equal((int)status, exception.StatusCode);
        Assert.Contains("terminal", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("retryable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheClassification_SurvivesAProviderMessageLongEnoughToBeTruncated()
    {
        // The composed message is capped, because both artifact writers persist only its
        // first line. A verbose provider must not push "retryable" off the end of the text
        // an operator reads.
        var exception = await RejectionFromAsync(
            HttpStatusCode.TooManyRequests, "rate_limit_error", new string('x', 4_000));

        Assert.True(exception.IsRetryable);
        Assert.Contains("retryable", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= OperatorFacingText.MaxLength + 1);
    }

    [Fact]
    public async Task TheStreamingPath_ClassifiesTheSameWayAsTheNonStreamingOne()
    {
        // Query streams; Ingest and Lint do not. The two paths catch the provider
        // rejection at different places, and both must reach the same verdict.
        var streaming = await RejectionFromAsync(
            HttpStatusCode.TooManyRequests, "rate_limit_error", "Slow down.", streaming: true);

        Assert.True(streaming.IsRetryable);
        Assert.Equal(429, streaming.StatusCode);
        Assert.Contains("retryable", streaming.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryableRejection_StillCarriesTheProvidersOwnExplanation()
    {
        // The classification is added to the existing operator-facing message (FR-006),
        // never in place of the detail that message exists to carry.
        const string detail = "This request would exceed your organization's rate limit.";
        var exception = await RejectionFromAsync(
            HttpStatusCode.TooManyRequests, "rate_limit_error", detail);

        Assert.Contains("429", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rate_limit_error", exception.Message, StringComparison.Ordinal);
        Assert.Contains(detail, exception.Message, StringComparison.Ordinal);
        Assert.Equal("rate_limit_error", exception.ErrorType);
    }

    [Fact]
    public async Task AnUnparseableErrorBody_IsStillClassifiedFromTheStatus()
    {
        // A gateway in front of the provider answers HTML, not the error envelope. The
        // status alone is enough to tell "wait" from "never".
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>");

        var exception = await Assert.ThrowsAsync<ModelApiException>(
            () => NextTurnAgainstAsync(provider, streaming: false));

        Assert.True(exception.IsRetryable);
        Assert.Equal(502, exception.StatusCode);
        Assert.Null(exception.ErrorType);
    }

    private static async Task<ModelApiException> RejectionFromAsync(
        HttpStatusCode status, string errorType, string message, bool streaming = false)
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            status, FakeAnthropicEndpoint.ErrorBody(errorType, message));

        return await Assert.ThrowsAsync<ModelApiException>(
            () => NextTurnAgainstAsync(provider, streaming));
    }

    private static async Task<ModelTurn> NextTurnAgainstAsync(
        FakeAnthropicEndpoint provider, bool streaming)
    {
        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var client = new AnthropicModelClient(
            logger: null!,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar);

        return await client.NextTurnAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            ToolRegistry.Default.Tools,
            CancellationToken.None,
            onTextDelta: streaming ? _ => { } : null);
    }
}
