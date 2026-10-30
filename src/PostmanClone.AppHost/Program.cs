using PostmanClone.AppHost.Resources;

// The composition root (architecture-standards P1). Wiring lives in the Resources extension methods;
// this file is the manifest. Each work package adds its resource lines here:
//   WP04 CollectionsService, WP05 EnvironmentsService, WP06 ExecutionService, WP07 Web.
var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL server, one logical database per owning service (P3). Names are the services' own.
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var authDatabase = postgres.AddDatabase("authdb");
var collectionsDatabase = postgres.AddDatabase("collectionsdb");
var environmentsDatabase = postgres.AddDatabase("environmentsdb");
var executionDatabase = postgres.AddDatabase("executiondb");

var authService = builder.AddAuthService(authDatabase);

builder.AddCollectionsService(collectionsDatabase, authService);
var environmentsService = builder.AddEnvironmentsService(environmentsDatabase, authService);
builder.AddExecutionService(executionDatabase, authService, environmentsService);
builder.AddWeb(authService);

builder.Build().Run();
