using Veyra.Alerting;
using Veyra.Ingestion;
using Veyra.Query;
using Veyra.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddVeyraIngestion(builder.Configuration)
    .AddVeyraStorage(builder.Configuration)
    .AddVeyraQuery(builder.Configuration)
    .AddVeyraAlerting(builder.Configuration);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.Run();

/// <summary>Exposed so integration tests can host the application with WebApplicationFactory.</summary>
public partial class Program;
