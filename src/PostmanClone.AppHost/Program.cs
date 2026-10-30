using PostmanClone.AppHost.Resources;

// The composition root (architecture-standards P1). Wiring lives in the Resources extension methods;
// this file is the manifest. Later work packages add one resource line each:
//   WP04 CollectionsService, WP07 Web.
var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL server, one logical database per owning service (P3). Names are the services' own.
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var authDatabase = postgres.AddDatabase("authdb");
var collectionsDatabase = postgres.AddDatabase("collectionsdb");
var environmentsDatabase = postgres.AddDatabase("environmentsdb");
var executionDatabase = postgres.AddDatabase("executiondb");

var authService = builder.AddAuthService(authDatabase);

var environmentsService = builder.AddEnvironmentsService(environmentsDatabase, authService);
builder.AddExecutionService(executionDatabase, authService, environmentsService);

// WP04: builder.AddCollectionsService(collectionsDatabase);
// WP07: builder.AddWeb(...);

builder.Build().Run();
