using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AiShell.IO;

public sealed class CommandExecutor
{
    public async Task<CommandExecutionResult> ExecuteAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null)
    {
        var startInfo = CreateStartInfo(command, workingDirectory);
        var outputStreamed = standardOutput is not null || standardError is not null;

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardOutput?.WriteLine(args.Data);
                outputBuilder.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardError?.WriteLine(args.Data);
                errorBuilder.AppendLine(args.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new CommandExecutionResult(
                    -1,
                    string.Empty,
                    "Impossibile avviare il processo.",
                    false,
                    outputStreamed);
            }
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult(
                -1,
                string.Empty,
                $"Errore di avvio del processo: {ex.Message}",
                false,
                outputStreamed);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process);
            try
            {
                await process.WaitForExitAsync();
            }
            catch
            {
                process.WaitForExit(1000);
            }

            return new CommandExecutionResult(
                -1,
                outputBuilder.ToString(),
                errorBuilder.ToString(),
                true,
                outputStreamed
            );
        }

        return new CommandExecutionResult(
            process.ExitCode,
            outputBuilder.ToString(),
            errorBuilder.ToString(),
            false,
            outputStreamed
        );
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Termination errors are ignored because they typically occur if:
            // - The process has already exited (ObjectDisposedException, InvalidOperationException)
            // - Access is denied (AccessDeniedException)
            // In the context of cancellation, it's safe to ignore these errors since the process is either already gone or cannot be forcibly terminated,
            // and failure to terminate does not affect the overall cancellation logic.
        }
    }

    private static ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.FileName = "/bin/bash";
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);
        }

        return startInfo;
    }
}
