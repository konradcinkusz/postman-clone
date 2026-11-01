using PostmanClone.Agent.Cli;

// The local agent (ADR-0026, CONTRACTS A2.6). The commands are in AgentCli; this file only starts them.
var exitCode = await AgentCli.RunAsync(args);
return exitCode;

/// <summary>Lets the tests start the entry point.</summary>
public partial class Program;
