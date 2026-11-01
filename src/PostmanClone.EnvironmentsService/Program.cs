using PostmanClone.EnvironmentsService;
using PostmanClone.EnvironmentsService.Endpoints;
using PostmanClone.ServiceDefaults;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.Security;

// The composition root of this service (P1). Each step is one extension method; wiring lives there, not here.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddEnvironmentsService();

var app = builder.Build();

app.UseProblemDetailsDefaults();
app.UseSecurityDefaults();
app.MapEnvironmentEndpoints();
app.MapAgentEndpoints();
app.MapDefaultEndpoints();

app.Run();

// Lets WebApplicationFactory<Program> start this entry point in tests.
public partial class Program;
