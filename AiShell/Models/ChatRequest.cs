using System.Text.Json.Serialization;

namespace AiShell.Models;

public sealed record ChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IEnumerable<ChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream = false
);
