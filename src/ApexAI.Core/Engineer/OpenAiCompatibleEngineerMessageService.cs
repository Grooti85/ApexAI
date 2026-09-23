using System.Net.Http.Json;
using ApexAI.Core.Configuration;
using ApexAI.Core.Race;

namespace ApexAI.Core.Engineer;

public sealed class OpenAiCompatibleEngineerMessageService : IEngineerMessageService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly IEngineerMessageService _fallback;

    public OpenAiCompatibleEngineerMessageService(HttpClient http, EngineerSettings settings, string apiKey,
        IEngineerMessageService? fallback = null)
    {
        _http = http;
        _endpoint = settings.Endpoint;
        _model = settings.Model;
        _apiKey = apiKey;
        _fallback = fallback ?? new DeterministicEngineerMessageService();
    }

    public string GetMessage(RaceEvent raceEvent)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            request.Headers.Authorization = new("Bearer", _apiKey);
            request.Content = JsonContent.Create(new Request(_model, new[]
            {
                new Message("system", "You are a concise, safety-first sim racing engineer. Never suggest game automation or cheating."),
                new Message("user", raceEvent.Message)
            }));
            using var response = _http.Send(request);
            if (!response.IsSuccessStatusCode) return _fallback.GetMessage(raceEvent);
            var result = response.Content.ReadFromJsonAsync<Response>().GetAwaiter().GetResult();
            return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() is { Length: > 0 } text
                ? text : _fallback.GetMessage(raceEvent);
        }
        catch (HttpRequestException) { return _fallback.GetMessage(raceEvent); }
        catch (TaskCanceledException) { return _fallback.GetMessage(raceEvent); }
        catch (JsonException) { return _fallback.GetMessage(raceEvent); }
    }

    private sealed record Request(string Model, IReadOnlyList<Message> Messages);
    private sealed record Message(string Role, string Content);
    private sealed record Response(IReadOnlyList<Choice>? Choices);
    private sealed record Choice(Message? Message);
}
