using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RateLimiter.Api.Policies;
using RateLimiter.Api.Services;

namespace RateLimiter.Api.Middleware;

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitEvaluationService _evaluationService;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IRateLimitEvaluationService evaluationService,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _evaluationService = evaluationService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        var metadata = endpoint.Metadata.GetMetadata<RateLimitEndpointMetadata>();
        if (metadata is null || metadata.ExecutionMode == RateLimitExecutionMode.EndpointFilter)
        {
            await _next(context);
            return;
        }

        var evaluation = await _evaluationService.EvaluateAsync(context, metadata, context.RequestAborted).ConfigureAwait(false);
        if (evaluation is null)
        {
            await _next(context);
            return;
        }

        RateLimitResponseWriter.WriteHeaders(context.Response, evaluation.Decision);
        RateLimitResponseWriter.SetDecision(context, evaluation.Decision);

        if (evaluation.Decision.IsAllowed)
        {
            await _next(context);
            return;
        }

        _logger.LogDebug("Rate limit denied for policy {Policy}.", evaluation.Policy.PolicyName);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            var result = RateLimitResponseWriter.CreateProblem(context, evaluation.Decision, evaluation.Policy.PolicyName);
            await result.ExecuteAsync(context);
        }
    }
}
