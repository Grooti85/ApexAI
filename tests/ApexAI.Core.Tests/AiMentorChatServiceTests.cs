using System.Net;
using System.Text;
using System.Text.Json;
using ApexAI.Core.Configuration;
using ApexAI.Core.Engineer;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class AiMentorChatServiceTests
{
    private const string ValidResponse = """{"choices":[{"message":{"content":"Try three clean laps."}}]}""";

    [Fact]
    public async Task SendsOpenAiCompatibleChatRequestWithContextAndApiKey()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var http = new HttpClient(new FakeHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(ValidResponse);
        }));
        var service = new AiMentorChatService(http, ConfiguredSettings(),
            "secret-test-key");

        var answer = await service.AskAsync("Recorded lap: 2:00.500. Fuel unavailable.",
            [new MentorChatMessage("user", "How was my lap?"),
             new MentorChatMessage("assistant", "It was recorded at 2:00.500.")],
            "What should I focus on?");

        Assert.Equal("Try three clean laps.", answer);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-test-key", capturedRequest.Headers.Authorization.Parameter);
        Assert.Equal("https://example.test/v1/chat/completions", capturedRequest.RequestUri!.ToString());
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("Fuel unavailable", messages[0].GetProperty("content").GetString());
        Assert.Contains("Personalized driving coaching requires recorded ACC session telemetry",
            messages[0].GetProperty("content").GetString());
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("What should I focus on?", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OmitsAuthorizationHeaderWhenNoKeyIsProvidedForLocalProvider()
    {
        HttpRequestMessage? capturedRequest = null;
        using var http = new HttpClient(new FakeHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(ValidResponse));
        }));
        var settings = ConfiguredSettings() with
        {
            Endpoint = "http://localhost:11434/v1/chat/completions"
        };

        var answer = await new AiMentorChatService(http, settings, null)
            .AskAsync("facts", [], "question");

        Assert.Equal("Try three clean laps.", answer);
        Assert.Null(capturedRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task LocalOllamaNeverSendsAStoredHostedProviderKey()
    {
        HttpRequestMessage? capturedRequest = null;
        using var http = new HttpClient(new FakeHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(ValidResponse));
        }));
        var settings = new EngineerSettings(
            EngineerProvider.LocalOllama, "http://127.0.0.1:11434/v1/chat/completions", "qwen2.5:3b");

        await new AiMentorChatService(http, settings, "old-hosted-key").AskAsync("facts", [], "question");

        Assert.Null(capturedRequest!.Headers.Authorization);
    }

    [Theory]
    [InlineData("https://provider.example/v1/chat/completions")]
    [InlineData("http://provider.example/v1/chat/completions")]
    public async Task LocalOllamaRejectsNonLoopbackEndpoints(string endpoint)
    {
        var requestSent = false;
        using var http = new HttpClient(new FakeHandler((_, _) =>
        {
            requestSent = true;
            return Task.FromResult(JsonResponse(ValidResponse));
        }));
        var settings = new EngineerSettings(EngineerProvider.LocalOllama, endpoint, "qwen2.5:3b");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AiMentorChatService(http, settings, null).AskAsync("facts", [], "question"));

        Assert.False(requestSent);
    }

    [Fact]
    public async Task RejectsPlainHttpRemoteEndpointBeforeSendingRequest()
    {
        var requestSent = false;
        using var http = new HttpClient(new FakeHandler((_, _) =>
        {
            requestSent = true;
            return Task.FromResult(JsonResponse(ValidResponse));
        }));
        var settings = ConfiguredSettings() with { Endpoint = "http://provider.example/v1/chat/completions" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AiMentorChatService(http, settings, "key").AskAsync("facts", [], "question"));

        Assert.False(requestSent);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Check the provider key")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.BadGateway, "HTTP 502")]
    public async Task ReportsProviderHttpErrorsTruthfully(HttpStatusCode status, string expectedText)
    {
        using var http = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status))));

        var exception = await Assert.ThrowsAsync<MentorProviderException>(() =>
            new AiMentorChatService(http, ConfiguredSettings(), "key").AskAsync("facts", [], "question"));

        Assert.Contains(expectedText, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":" "}}]}""")]
    public async Task RejectsInvalidOrEmptyProviderResponses(string body)
    {
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(body))));

        await Assert.ThrowsAsync<MentorProviderException>(() =>
            new AiMentorChatService(http, ConfiguredSettings(), null).AskAsync("facts", [], "question"));
    }

    [Fact]
    public async Task ReportsConnectionErrorsWithoutFallingBackToTemplateText()
    {
        using var http = new HttpClient(new FakeHandler((_, _) =>
            throw new HttpRequestException("private transport detail")));

        var exception = await Assert.ThrowsAsync<MentorProviderException>(() =>
            new AiMentorChatService(http, ConfiguredSettings(), null).AskAsync("facts", [], "question"));

        Assert.Contains("Could not reach", exception.Message);
        Assert.DoesNotContain("private transport detail", exception.Message);
    }

    [Fact]
    public async Task AppliesConfiguredRequestTimeout()
    {
        using var http = new HttpClient(new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse(ValidResponse);
        }));
        var settings = ConfiguredSettings() with { AiTimeoutSeconds = 5 };

        var exception = await Assert.ThrowsAsync<MentorProviderException>(() =>
            new AiMentorChatService(http, settings, null).AskAsync("facts", [], "question"));

        Assert.Contains("timed out", exception.Message);
    }

    [Fact]
    public async Task PreservesCallerCancellation()
    {
        using var http = new HttpClient(new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse(ValidResponse);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AiMentorChatService(http, ConfiguredSettings(), null)
                .AskAsync("facts", [], "question", cancellation.Token));
    }

    [Fact]
    public async Task RefusesRequestsWhenProviderIsOffline()
    {
        using var http = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(JsonResponse(ValidResponse))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AiMentorChatService(http, new EngineerSettings(EngineerProvider.Offline), null)
                .AskAsync("facts", [], "question"));

        Assert.Contains("offline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static EngineerSettings ConfiguredSettings() =>
        new(EngineerProvider.OpenAiCompatible, "https://example.test/v1/chat/completions", "test-model");

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
