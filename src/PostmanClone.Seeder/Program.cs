using PostmanClone.Seeder;

// The manifest (P1): read the options, apply the gate, run the one command, set the exit code.
//   PostmanClone.Seeder            seed (the default)
//   PostmanClone.Seeder reset      remove the demo collections and environments (names under "[demo] ") from the demo workspace
// Exit codes: 0 done, 2 refused by the gate, 1 failed.
var command = args.FirstOrDefault() ?? "seed";
var options = SeedOptions.Read(Environment.GetEnvironmentVariable);

if (options.Refusal() is { } refusal)
{
    Console.Error.WriteLine("seed: REFUSED. " + refusal);
    return 2;
}

if (command is not ("seed" or "reset"))
{
    Console.Error.WriteLine("usage: PostmanClone.Seeder [seed|reset]");
    return 2;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var runner = new SeedRunner(options, new ApiClient(http), line => Console.WriteLine("seed: " + line), TimeProvider.System);
try
{
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
    if (command == "reset")
    {
        await runner.ResetAsync(cancel.Token);
    }
    else
    {
        var summary = await runner.SeedAsync(cancel.Token);
        Console.WriteLine($"seed: done ({summary.TotalCreated} item(s) created; 0 means the estate was already seeded)");
    }

    return 0;
}
catch (Exception ex) when (ex is SeedApiException or TimeoutException or InvalidOperationException or HttpRequestException)
{
    Console.Error.WriteLine("seed: FAILED. " + ex.Message);
    return 1;
}
