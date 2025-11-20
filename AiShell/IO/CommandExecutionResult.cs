namespace AiShell.IO;

public readonly record struct CommandExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool WasCanceled = false,
    bool OutputStreamed = false
);
