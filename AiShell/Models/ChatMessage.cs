using System.Text.Json.Serialization;

namespace AiShell.Models;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);
