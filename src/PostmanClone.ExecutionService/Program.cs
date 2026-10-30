using PostmanClone.ExecutionService;
using PostmanClone.ExecutionService.Endpoints;
using PostmanClone.ServiceDefaults;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.ServiceDefaults.Security;

// The manifest (P2, AGENTS.md): every piece of wiring lives in an extension method.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRateLimitingDefaults(builder.Configuration);
builder.AddExecutionService();

var app = builder.Build();

app.UseProblemDetailsDefaults();
app.UseSecurityDefaults();
app.MapExecutionEndpoints();
app.MapDefaultEndpoints();

app.Run();

// Lets the test project start the service with WebApplicationFactory<Program>.
public partial class Program { }
