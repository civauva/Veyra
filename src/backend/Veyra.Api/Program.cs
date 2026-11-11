using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access;
using Veyra.Alerting;
using Veyra.Ingestion;
using Veyra.Query;
using Veyra.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddVeyraIngestion(builder.Configuration)
    .AddVeyraStorage(builder.Configuration)
    .AddVeyraQuery(builder.Configuration)
    .AddVeyraAlerting(builder.Configuration)
    .AddVeyraAccess(builder.Configuration);

// Errors are answered as problem details (RFC 9457).
builder.Services.AddProblemDetails();

// The REST API uses controllers. Every module adds its controllers as an application part and registers how to
// build them, so the services behind them can stay internal to the module.
// Every request that changes state must carry the antiforgery header (ADR 0008).
builder.Services
    .AddControllers(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddApplicationPart(typeof(AccessServiceCollectionExtensions).Assembly)
    .AddApplicationPart(typeof(AlertingServiceCollectionExtensions).Assembly)
    .AddApplicationPart(typeof(QueryServiceCollectionExtensions).Assembly)
    .AddControllersAsServices()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The antiforgery filter above lives in MVC's view features. Veyra renders no views; this only registers the filter.
builder.Services.AddMvcCore().AddViews();

// Behind Caddy, the client address and the original scheme arrive in X-Forwarded-* headers. Veyra is published on
// 127.0.0.1 and inside the Compose network only, so every request it receives came through the reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Bring the database schema up to date before the first request can arrive, then open first-run setup if needed.
// Without storage there are no users to check; /readyz reports the missing configuration instead.
if (await app.Services.MigrateVeyraStorageAsync(app.Lifetime.ApplicationStopping))
{
    await app.Services.PrepareVeyraAccessAsync(app.Lifetime.ApplicationStopping);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

// Security headers on every response. The UI loads only its own scripts, styles, and API (ADR 0008). Inline styles
// are allowed because React sets style attributes; inline scripts are not.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "same-origin";

    await next(context);
});

// The built frontend from wwwroot (ADR 0004), served before authentication so the sign-in page can load.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Liveness: the process is up. It never checks dependencies, so a database outage does not restart the container.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .AllowAnonymous();

// Readiness: the instance can serve requests, which today means PostgreSQL is reachable.
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(StorageHealthChecks.ReadinessTag),
})
    .WithName("Ready")
    .AllowAnonymous();

app.MapControllers();

// OTLP receivers. They check ingestion keys themselves (ADR 0008).
app.MapVeyraOtlp();

// Every other path is a route of the single-page app, except the API and OTLP, which answer 404 for unknown paths.
// nonfile keeps paths with a file extension out: they belong to UseStaticFiles, and a missing file is a 404, not
// the app. Without it the fallback matches /assets/app.js first, and the static file middleware steps aside.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|v1/).*$)}", "index.html")
    .AllowAnonymous();

// The catch-all above does not match the empty path, so the start page gets its own route.
app.MapFallbackToFile("/", "index.html")
    .AllowAnonymous();

await app.RunAsync();

/// <summary>Exposed so integration tests can host the application with WebApplicationFactory.</summary>
public partial class Program;
