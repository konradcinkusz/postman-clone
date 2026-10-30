using PostmanClone.ServiceDefaults;
using PostmanClone.Web.Components;
using PostmanClone.Web.Hosting;

// The Web manifest (BLAZOR-WEB-APP §3, §8; P9). Each concern is one extension method in Hosting/ or Api/;
// nothing is wired inline here. The order of UseWebPipeline is the security order: forwarded headers, then
// the browser guards, then antiforgery, before the component endpoints.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebServices();

var app = builder.Build();

app.UseWebPipeline();
app.MapWebComponents();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Lets the Web tests host this manifest with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program { }
