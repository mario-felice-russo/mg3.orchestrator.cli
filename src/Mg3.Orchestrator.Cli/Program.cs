using System.Runtime.InteropServices;
using Mg3.Orchestrator.Cli.Hosting;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancellation.Cancel();
});

return await CliApplication.RunAsync(args, CliEnvironment.FromProcess(), cancellation.Token).ConfigureAwait(false);
