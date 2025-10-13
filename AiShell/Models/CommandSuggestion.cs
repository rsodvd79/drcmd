using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiShell.Models;

public sealed record CommandSuggestion
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    public static bool TryParse(string content, out CommandSuggestion suggestion, out string? error)
    {
        suggestion = default!;
        error = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Risposta vuota dal modello.";
            return false;
        }

        if (!TryExtractJson(content, out var jsonSegment))
        {
            error = "Impossibile estrarre un JSON valido dalla risposta del modello.";
            return false;
        }

        try
        {
            suggestion = JsonSerializer.Deserialize<CommandSuggestion>(jsonSegment, SerializerOptions)
                         ?? new CommandSuggestion { Command = null, Explanation = string.Empty, Confidence = 0 };
        }
        catch (JsonException ex)
        {
            error = $"Errore di parsing JSON: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(suggestion.Explanation))
        {
            error = "Il campo 'explanation' non è presente o è vuoto.";
            return false;
        }

        suggestion = suggestion with { Confidence = Math.Clamp(suggestion.Confidence, 0, 1) };
        return true;
    }

    private static bool TryExtractJson(string content, out string jsonSegment)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');

        if (start < 0 || end < start)
        {
            jsonSegment = string.Empty;
            return false;
        }

        jsonSegment = content[start..(end + 1)];
        return true;
    }
}
