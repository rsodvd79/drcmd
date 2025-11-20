using System;

namespace AiShell;

public sealed record ShellOptions(string Model, string Endpoint)
{
    private const string DefaultModel = "gemma3:1b";
    private const string DefaultEndpoint = "http://localhost:11434";

    public static ShellOptions FromArgs(string[] args)
    {
        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? DefaultModel;
        var endpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? DefaultEndpoint;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryReadOption(arg, "--model", "-m", args, ref i, out var modelValue))
            {
                model = modelValue;
                continue;
            }

            if (TryReadOption(arg, "--endpoint", "-e", args, ref i, out var endpointValue))
            {
                endpoint = endpointValue;
            }
        }

        return new ShellOptions(NormalizeModel(model), NormalizeEndpoint(endpoint));
    }

    private static bool TryReadOption(string arg, string longName, string shortName, string[] args, ref int index, out string value)
    {
        value = string.Empty;

        if (arg.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(longName.Length + 1)..];
            return true;
        }

        if (arg.Equals(longName, StringComparison.OrdinalIgnoreCase) || arg.Equals(shortName, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 < args.Length)
            {
                value = args[index + 1];
                index++;
                return true;
            }

            return false;
        }

        return false;
    }

    private static string NormalizeModel(string? model)
    {
        return string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    private static string NormalizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return DefaultEndpoint;
        }

        var trimmed = endpoint.Trim().TrimEnd('/');

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.ToString();
        }

        if (Uri.TryCreate($"http://{trimmed}", UriKind.Absolute, out var httpUri))
        {
            return httpUri.ToString();
        }

        return DefaultEndpoint;
    }
}
