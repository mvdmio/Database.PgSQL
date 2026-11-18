using mvdmio.Database.PgSQL.Tool;
using mvdmio.Database.PgSQL.Tool.Commands;
using System.CommandLine;

var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
   e.Cancel = true;

   if (cancellation.IsCancellationRequested)
      Environment.Exit(ExitCodes.CANCELLED);

   cancellation.Cancel();
};

var configuration = new InvocationConfiguration { ProcessTerminationTimeout = null };

return await RootCommandFactory.Create().Parse(args).InvokeAsync(configuration, cancellation.Token);
