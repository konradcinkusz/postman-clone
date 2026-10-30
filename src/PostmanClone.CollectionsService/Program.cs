using PostmanClone.CollectionsService;
using PostmanClone.CollectionsService.Api;

// The composition root of CollectionsService (architecture-standards P1, P2). Wiring lives in the extension
// methods; this file is the manifest.
var builder = WebApplication.CreateBuilder(args);
builder.AddCollectionsService();

var app = builder.Build();
app.UseCollectionsService();
app.MapCollectionsApi();
app.Run();

// Lets the integration tests host this program with WebApplicationFactory.
public partial class Program;
