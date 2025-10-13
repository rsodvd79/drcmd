using AiShell;
using AiShell.IO;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellationSource.Cancel();
};

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:11434", UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(120)
};

var shell = new AiCommandShell(new OllamaClient(httpClient), new CommandExecutor());
await shell.RunAsync(cancellationSource.Token);
