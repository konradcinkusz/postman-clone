using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Services;
using PostmanClone.ServiceDefaults;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.ServiceDefaults.Problems;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.ServiceDefaults.Security;

namespace PostmanClone.CollectionsService;

/// <summary>Registers what CollectionsService needs: the kernel, authentication, rate limits and the collectionsdb context.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The connection name of this service's database, from the AppHost (<c>ConnectionStrings:collectionsdb</c>).</summary>
    public const string DatabaseConnectionName = "collectionsdb";

    public static WebApplicationBuilder AddCollectionsService(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddRateLimitingDefaults(builder.Configuration);
        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddScoped<ICollectionService, CollectionService>();
        builder.Services.AddScoped<IFolderService, FolderService>();
        builder.Services.AddScoped<IRequestService, RequestService>();

        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddOpenApi();
        }

        builder.AddDatabaseContext<CollectionsDbContext>(DatabaseConnectionName);
        return builder;
    }

    /// <summary>The request pipeline: problem details, then the kernel's security headers, origin guard, authentication, rate limits and authorization.</summary>
    public static WebApplication UseCollectionsService(this WebApplication app)
    {
        app.UseProblemDetailsDefaults();
        app.UseSecurityDefaults();
        return app;
    }
}
