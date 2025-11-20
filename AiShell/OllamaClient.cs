using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiShell.Models;

namespace AiShell;

public sealed class OllamaClient
{
    private readonly string _model;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;

    public OllamaClient(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = string.IsNullOrWhiteSpace(model) ? "gemma3:1b" : model;
    }

    public async Task<string?> SendChatAsync(IEnumerable<ChatMessage> conversation, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(_model, conversation);
        var payload = JsonSerializer.Serialize(request, SerializerOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return $"{{\"command\":null,\"explanation\":\"Errore Ollama: {response.StatusCode} - {error}\",\"confidence\":0}}";
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var chatResponse = await JsonSerializer.DeserializeAsync<ChatResponse>(stream, SerializerOptions, cancellationToken);
            return chatResponse?.Message?.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            return $"{{\"command\":null,\"explanation\":\"Impossibile contattare Ollama: {ex.Message}\",\"confidence\":0}}";
        }
    }
}
