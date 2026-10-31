using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostmanClone.CollectionsService.Data;
using PostmanClone.CollectionsService.Interop.Postman;
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
        builder.Services.AddScoped<IPostmanIoService, PostmanIoService>();

        // The multipart import is read as one file; a form larger than the file limit plus its envelope is refused while it is read.
        builder.Services.Configure<FormOptions>(options =>
            options.MultipartBodyLengthLimit = PostmanImportLimits.MaxFileBytes + PostmanImportLimits.MaxMultipartEnvelopeBytes);

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
