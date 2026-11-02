using PostmanClone.AppHost.Resources;

// The composition root (architecture-standards P1). Wiring lives in the Resources extension methods;
// this file is the manifest. Each work package adds its resource lines here:
//   WP04 CollectionsService, WP05 EnvironmentsService, WP06 ExecutionService, WP07 Web, ADR-0030 the demo seed.
var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL server, one logical database per owning service (P3). Names are the services' own.
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var authDatabase = postgres.AddDatabase("authdb");
var collectionsDatabase = postgres.AddDatabase("collectionsdb");
var environmentsDatabase = postgres.AddDatabase("environmentsdb");
var executionDatabase = postgres.AddDatabase("executiondb");

var authService = builder.AddAuthService(authDatabase);

var collectionsService = builder.AddCollectionsService(collectionsDatabase, authService);
var environmentsService = builder.AddEnvironmentsService(environmentsDatabase, authService);
builder.AddExecutionService(executionDatabase, authService, environmentsService);
builder.AddWeb(authService);

// The demo seed (ADR-0030): Development only, on unless Seed:Enabled=false. It waits for the services, creates the demo data through
// their APIs and exits. compose.yaml (ADR-0031) runs the same seeder after the same services.
if (SeedResource.IsEnabled(builder))
{
    builder.AddSeed(authService, collectionsService, environmentsService);
}

builder.Build().Run();
