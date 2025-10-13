using System.Text.Json.Serialization;

namespace AiShell.Models;

public sealed class ChatResponse
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
