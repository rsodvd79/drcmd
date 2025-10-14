using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using AiShell.IO;
using AiShell.Models;

namespace AiShell;

public sealed class AiCommandShell
{
    private static readonly string[] ExitCommands = ["exit", "quit", ":q", "bye"];
    private readonly OllamaClient _ollamaClient;
    private readonly CommandExecutor _commandExecutor;
    private readonly List<ChatMessage> _conversation;
    private readonly List<string> _commandHistory;
    private string _currentDirectory;
    private bool _autoAcceptAiCommands;
    private int _historyPosition;

    public AiCommandShell(OllamaClient ollamaClient, CommandExecutor commandExecutor)
    {
        _ollamaClient = ollamaClient;
        _commandExecutor = commandExecutor;
        _conversation =
        [
            new ChatMessage(
                "system",
                """
                Sei un assistente che aiuta l'utente a usare il terminale Unix. Rispondi esclusivamente con un oggetto JSON. Il formato deve essere:
                {
                  "command": string|null,
                  "explanation": string,
                  "confidence": number
                }
                - "command" contiene il comando da eseguire senza spazi iniziali, oppure null se non devi proporre un comando.
                - "explanation" descrive in modo sintetico cosa fa il comando o perché non è possibile fornirlo.
                - "confidence" è un valore tra 0 e 1 che rappresenta quanto sei sicuro che il comando sia corretto.
                Non aggiungere testo fuori dal JSON, né blocchi di codice.
                """
            )
        ];
        _commandHistory = [];
        _currentDirectory = Directory.GetCurrentDirectory();
        _autoAcceptAiCommands = false;
        _historyPosition = 0;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        PrintWelcomeBanner();

        while (!cancellationToken.IsCancellationRequested)
        {
            _currentDirectory = Directory.GetCurrentDirectory();
            var prompt = BuildPrompt();
            Console.Write(prompt);
            var input = ReadInputLine(prompt);

            if (input is null)
            {
                break;
            }

            input = input.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (ExitCommands.Contains(input, StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(input, "help", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                continue;
            }

            if (input.StartsWith('#'))
            {
                await HandleAiSuggestionAsync(input[1..].Trim(), cancellationToken);
                continue;
            }

            await ExecuteDirectCommandAsync(input, cancellationToken);
        }

        Console.WriteLine("Arrivederci!");
    }

    private async Task HandleAiSuggestionAsync(string userInput, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            Console.WriteLine("⚠️  Specifica una richiesta dopo '#'.");
            return;
        }

        var userMessage = new ChatMessage("user", userInput);
        _conversation.Add(userMessage);

        var conversationWithEnvironment = BuildConversationWithEnvironment();

        var responseContent = await _ollamaClient.SendChatAsync(conversationWithEnvironment, cancellationToken);
        if (responseContent is null)
        {
            _conversation.Remove(userMessage);
            Console.WriteLine("⚠️  Nessuna risposta ricevuta dal modello. Controlla che Ollama sia in esecuzione.");
            return;
        }

        var assistantMessage = new ChatMessage("assistant", responseContent);
        _conversation.Add(assistantMessage);

        if (!CommandSuggestion.TryParse(responseContent, out var suggestion, out var parseError))
        {
            Console.WriteLine("⚠️  Risposta del modello non valida:");
            Console.WriteLine(responseContent);
            if (!string.IsNullOrWhiteSpace(parseError))
            {
                Console.WriteLine($"Dettagli: {parseError}");
            }
            return;
        }

        PresentSuggestion(suggestion);
        if (string.IsNullOrWhiteSpace(suggestion.Command))
        {
            return;
        }

        if (_autoAcceptAiCommands)
        {
            await ExecuteDirectCommandAsync(suggestion.Command!, cancellationToken);
            return;
        }

        Console.Write("Eseguo il comando suggerito? [Y/n]: ");
        var confirmation = Console.ReadLine();
        if (!IsAffirmative(confirmation))
        {
            return;
        }

        await ExecuteDirectCommandAsync(suggestion.Command!, cancellationToken);
    }

    private async Task ExecuteDirectCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            Console.WriteLine("⚠️  Nessun comando da eseguire.");
            return;
        }

        AddToHistory(command);

        if (TryHandleBuiltInCommand(command))
        {
            return;
        }

        var result = await _commandExecutor.ExecuteAsync(command, _currentDirectory, cancellationToken);
        PrintCommandResult(result);
    }

    private List<ChatMessage> BuildConversationWithEnvironment()
    {
        var environmentMessage = new ChatMessage("system", BuildEnvironmentDescription());

        if (_conversation.Count == 0)
        {
            return new List<ChatMessage> { environmentMessage };
        }

        var conversation = new List<ChatMessage>(_conversation.Count + 1)
        {
            _conversation[0],
            environmentMessage
        };

        for (var i = 1; i < _conversation.Count; i++)
        {
            conversation.Add(_conversation[i]);
        }

        return conversation;
    }

    private static string BuildEnvironmentDescription()
    {
        var osDescription = RuntimeInformation.OSDescription;
        var architecture = RuntimeInformation.ProcessArchitecture;
        var framework = RuntimeInformation.FrameworkDescription;
        var currentDirectory = Directory.GetCurrentDirectory();

        return $"Contesto ambiente: sistema operativo={osDescription} ({architecture}), framework={framework}, directory_corrente={currentDirectory}.";
    }

    private string BuildPrompt()
    {
        var builder = new StringBuilder();
        var userHost = $"{Environment.UserName}@{Environment.MachineName}";
        var location = ShortenPath(_currentDirectory);

        builder.Append("┌─[");
        builder.Append(userHost);
        builder.Append("]─[");
        builder.Append(location);
        builder.Append(']');

        builder.AppendLine();
        builder.Append("└─$ ");

        return builder.ToString();
    }

    private string? ReadInputLine(string prompt)
    {
        var buffer = new StringBuilder();
        _historyPosition = _commandHistory.Count;

        while (true)
        {
            ConsoleKeyInfo keyInfo;
            try
            {
                keyInfo = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine();
                return buffer.Length == 0 ? null : buffer.ToString();
            }

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                if (_commandHistory.Count > 0)
                {
                    if (_historyPosition > 0)
                    {
                        _historyPosition--;
                    }
                    ReplaceInputBuffer(buffer, _commandHistory[_historyPosition]);
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                if (_commandHistory.Count > 0)
                {
                    if (_historyPosition < _commandHistory.Count)
                    {
                        _historyPosition++;
                    }

                    if (_historyPosition == _commandHistory.Count)
                    {
                        ReplaceInputBuffer(buffer, string.Empty);
                    }
                    else
                    {
                        ReplaceInputBuffer(buffer, _commandHistory[_historyPosition]);
                    }
                }
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length -= 1;
                    Console.Write("\b \b");
                }
                _historyPosition = _commandHistory.Count;
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Tab)
            {
                _historyPosition = _commandHistory.Count;
                HandleTabCompletion(buffer, prompt);
                continue;
            }

            if (IsEndOfTransmission(keyInfo))
            {
                if (buffer.Length == 0)
                {
                    Console.WriteLine();
                    return null;
                }

                continue;
            }

            var ch = keyInfo.KeyChar;
            if (!char.IsControl(ch))
            {
                buffer.Append(ch);
                Console.Write(ch);
                _historyPosition = _commandHistory.Count;
            }
        }
    }

    private void HandleTabCompletion(StringBuilder buffer, string prompt)
    {
        var input = buffer.ToString();
        var tokenStart = input.LastIndexOfAny(new[] { ' ', '\t' });
        tokenStart = tokenStart >= 0 ? tokenStart + 1 : 0;
        var token = input[tokenStart..];

        if (!TryGetCompletionContext(token, out var baseDirectory, out var partialName, out var pathPrefix))
        {
            return;
        }

        List<(string Name, bool IsDirectory)> matches;
        try
        {
            matches = Directory.EnumerateFileSystemEntries(baseDirectory)
                .Select(entry =>
                {
                    var name = Path.GetFileName(entry) ?? entry;
                    var isDirectory = false;
                    try
                    {
                        isDirectory = File.GetAttributes(entry).HasFlag(FileAttributes.Directory);
                    }
                    catch
                    {
                        // Ignora elementi non accessibili
                    }

                    return (Name: name, IsDirectory: isDirectory);
                })
                .Where(match => match.Name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return;
        }

        if (matches.Count == 0)
        {
            return;
        }

        if (matches.Count == 1)
        {
            var match = matches[0];
            var addition = match.Name[partialName.Length..];
            buffer.Append(addition);
            Console.Write(addition);
            if (match.IsDirectory)
            {
                buffer.Append(Path.DirectorySeparatorChar);
                Console.Write(Path.DirectorySeparatorChar);
            }
            return;
        }

        Console.WriteLine();
        foreach (var match in matches)
        {
            var display = pathPrefix + match.Name + (match.IsDirectory ? Path.DirectorySeparatorChar : string.Empty);
            Console.WriteLine(display);
        }
        Console.WriteLine();
        Console.Write(prompt);
        Console.Write(buffer.ToString());
    }

    private bool TryGetCompletionContext(string token, out string baseDirectory, out string partialName, out string pathPrefix)
    {
        token ??= string.Empty;
        var normalized = token.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var endsWithSeparator = normalized.Length > 0 && normalized[^1] == Path.DirectorySeparatorChar;

        if (endsWithSeparator)
        {
            pathPrefix = token;
            partialName = string.Empty;
            baseDirectory = ExpandPath(normalized);
            return Directory.Exists(baseDirectory);
        }

        var lastSeparator = normalized.LastIndexOf(Path.DirectorySeparatorChar);
        if (lastSeparator >= 0)
        {
            pathPrefix = token[..(lastSeparator + 1)];
            partialName = token[(lastSeparator + 1)..];
            var dirToken = normalized[..(lastSeparator + 1)];
            baseDirectory = ExpandPath(dirToken);
            return Directory.Exists(baseDirectory);
        }

        pathPrefix = string.Empty;
        partialName = token;
        baseDirectory = _currentDirectory;
        return Directory.Exists(baseDirectory);
    }

    private string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return _currentDirectory;
        }

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        if (normalized.Equals("~", StringComparison.Ordinal))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (normalized.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var relative = normalized[2..];
            return Path.GetFullPath(Path.Combine(home, relative));
        }

        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        return Path.GetFullPath(Path.Combine(_currentDirectory, normalized));
    }

    private static bool IsEndOfTransmission(ConsoleKeyInfo keyInfo)
    {
        return (keyInfo.Key == ConsoleKey.D || keyInfo.Key == ConsoleKey.Z)
               && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control);
    }

    private static void PresentSuggestion(CommandSuggestion suggestion)
    {
        var builder = new StringBuilder();
        builder.AppendLine("💡 Suggerimento dell'AI:");
        builder.AppendLine($"- Comando: {suggestion.Command ?? "(nessuno)"}");
        builder.AppendLine($"- Affidabilità: {suggestion.Confidence:P0}");
        builder.AppendLine($"- Note: {suggestion.Explanation}");
        Console.WriteLine(builder.ToString());
    }

    private static void PrintCommandResult(CommandExecutionResult result)
    {
        if (result.StandardOutput.Length > 0)
        {
            Console.Write(result.StandardOutput);
        }

        if (result.StandardError.Length > 0)
        {
            Console.Error.Write(result.StandardError);
        }
    }

    private static bool IsAffirmative(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var normalized = input.Trim();
        if (normalized.Equals("n", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("s", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("si", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintWelcomeBanner()
    {
        Console.WriteLine("==========================================");
        Console.WriteLine("  AI Shell con Ollama (modello gemma3:1b)  ");
        Console.WriteLine("==========================================");
        Console.WriteLine("Digita un comando per eseguirlo, oppure inizia con '#' per chiedere un suggerimento all'AI.");
        Console.WriteLine("Comandi utili: help, exit");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("help                Mostra questo messaggio.");
        Console.WriteLine("exit | quit | :q    Esce dalla shell.");
        Console.WriteLine("#<testo>            Chiede un suggerimento al modello gemma3:1b.");
        Console.WriteLine("<comando>           Esegue il comando direttamente tramite la shell.");
    }

    private bool TryHandleBuiltInCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith("auto_accetta", StringComparison.OrdinalIgnoreCase))
        {
            HandleAutoAcceptCommand(trimmed);
            return true;
        }

        if (!trimmed.StartsWith("cd", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Equals("cd", StringComparison.Ordinal))
        {
            ChangeDirectory(null);
            return true;
        }

        if (!char.IsWhiteSpace(trimmed[2]))
        {
            return false;
        }

        var argument = trimmed[2..].Trim();
        if (argument.Length == 0)
        {
            ChangeDirectory(null);
            return true;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            ChangeDirectory(null);
            return true;
        }

        if (parts.Length > 1)
        {
            Console.Error.WriteLine("cd: troppi argomenti.");
            return true;
        }

        ChangeDirectory(parts[0]);
        return true;
    }

    private void HandleAutoAcceptCommand(string command)
    {
        var suffix = command["auto_accetta".Length..].Trim();
        if (suffix.Length == 0)
        {
            _autoAcceptAiCommands = !_autoAcceptAiCommands;
        }
        else if (suffix.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            _autoAcceptAiCommands = true;
        }
        else if (suffix.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _autoAcceptAiCommands = false;
        }
        else
        {
            Console.WriteLine("Uso: auto_accetta [on|off]");
            return;
        }

        Console.WriteLine($"auto_accetta: {(_autoAcceptAiCommands ? "attivo" : "disattivo")}");
    }

    private bool ChangeDirectory(string? target)
    {
        var destination = ResolveDestination(target);
        if (destination is null)
        {
            Console.Error.WriteLine("cd: directory non valida.");
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(destination, _currentDirectory);
            if (!Directory.Exists(fullPath))
            {
                Console.Error.WriteLine($"cd: nessuna directory '{target}'.");
                return false;
            }

            Directory.SetCurrentDirectory(fullPath);
            _currentDirectory = Directory.GetCurrentDirectory();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cd: {ex.Message}");
            return false;
        }
    }

    private string? ResolveDestination(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var cleaned = target.Trim('"', '\'');

        if (cleaned.StartsWith("~/", StringComparison.Ordinal) || cleaned.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var relative = cleaned[2..];
            return Path.Combine(home, relative);
        }

        if (Path.IsPathRooted(cleaned))
        {
            return cleaned;
        }

        return Path.Combine(_currentDirectory, cleaned);
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home))
        {
            var normalizedHome = home.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (normalized.Equals(normalizedHome, StringComparison.OrdinalIgnoreCase))
            {
                return "~";
            }

            if (normalized.StartsWith(normalizedHome + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized[(normalizedHome.Length + 1)..];
                return relative.Length == 0 ? "~" : $"~{Path.DirectorySeparatorChar}{relative}";
            }
        }

        return normalized;
    }

    private void AddToHistory(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (_commandHistory.Count > 0
            && string.Equals(_commandHistory[^1], command, StringComparison.Ordinal))
        {
            return;
        }

        _commandHistory.Add(command);
    }

    private static void ReplaceInputBuffer(StringBuilder buffer, string content)
    {
        while (buffer.Length > 0)
        {
            Console.Write("\b \b");
            buffer.Length -= 1;
        }

        buffer.Clear();

        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        buffer.Append(content);
        Console.Write(content);
    }
}
