using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTelemetry.Instrumentation.Runtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using RateLimiter.Api.Extensions;
using RateLimiter.Api.Identity;
using RateLimiter.Api.Middleware;
using RateLimiter.Api.Policies;
using RateLimiter.Api.Services;
using RateLimiter.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
	configuration
		.ReadFrom.Configuration(context.Configuration)
		.ReadFrom.Services(services)
		.Enrich.FromLogContext()
		.Enrich.WithEnvironmentName()
		.Enrich.WithProperty("Application", "RateLimiter.Api")
		.WriteTo.Async(cfg => cfg.Console());
});

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiterInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IRateLimitIdentityExtractor, DefaultRateLimitIdentityExtractor>();
builder.Services.AddScoped<IRateLimitEvaluationService, RateLimitEvaluationService>();

builder.Services.AddOpenTelemetry()
	.ConfigureResource(resource => resource.AddService("RateLimiter.Api"))
	.WithMetrics(metrics =>
	{
	metrics.AddAspNetCoreInstrumentation();
	metrics.AddRuntimeInstrumentation();
	metrics.AddMeter("RateLimiter.Infrastructure");
	})
	.WithTracing(tracing =>
	{
		tracing.AddAspNetCoreInstrumentation();
		tracing.AddHttpClientInstrumentation();
		tracing.AddConsoleExporter();
	});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseRouting();

app.UseMiddleware<RateLimitingMiddleware>();

app.MapHealthChecks("/healthz");
app.MapMetrics();

app.MapGet("/", () => Results.Ok(new { status = "healthy" }));

var auth = app.MapGroup("/auth");
auth.MapPost("/login", (LoginRequest request) =>
	Results.Ok(new LoginResponse(Guid.NewGuid().ToString("N"), request.Username)))
	.RequireRateLimitPolicy("auth-login", RateLimitExecutionMode.EndpointFilter, identityHint: "api-key");

var api = app.MapGroup("/api");
api.MapGet("/search", (string query) =>
{
	var items = Enumerable.Range(1, 5)
		.Select(i => new SearchResult($"result-{i}", $"Match {i} for '{query}'"));
	return Results.Ok(new SearchResponse(query, items));
}).RequireRateLimitPolicy("search", RateLimitExecutionMode.Middleware);

api.MapPost("/reports/{id:int}/generate", (int id) =>
{
	return Results.Accepted($"/reports/{id}", new { reportId = id, status = "queued" });
}).RequireRateLimitPolicy("reports", RateLimitExecutionMode.Middleware, tokenSelector: static (_, _) => ValueTask.FromResult(5u));

app.Run();

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string Subject);

public record SearchResponse(string Query, IEnumerable<SearchResult> Items);

public record SearchResult(string Id, string Snippet);
