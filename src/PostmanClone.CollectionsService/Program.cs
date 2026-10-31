using PostmanClone.CollectionsService;
using PostmanClone.CollectionsService.Api;
using PostmanClone.ServiceDefaults;

// The composition root of CollectionsService (architecture-standards P1, P2). Wiring lives in the extension
// methods; this file is the manifest. The kernel's two entry points are called here, not from inside a service
// extension, so every service's manifest shows them (P2, checked by the architecture tests).
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCollectionsService();

var app = builder.Build();
app.UseCollectionsService();
app.MapCollectionsApi();
app.MapDefaultEndpoints();
app.Run();

// Lets the integration tests host this program with WebApplicationFactory.
public partial class Program;
