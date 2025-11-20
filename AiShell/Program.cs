using AiShell;
using AiShell.IO;

var options = ShellOptions.FromArgs(args);

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellationSource.Cancel();
};

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(options.Endpoint, UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(120)
};

var shell = new AiCommandShell(new OllamaClient(httpClient, options.Model), new CommandExecutor());
await shell.RunAsync(cancellationSource.Token);
