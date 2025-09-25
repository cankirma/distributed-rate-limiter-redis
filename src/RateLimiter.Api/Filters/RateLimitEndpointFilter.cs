using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using RateLimiter.Api.Policies;
using RateLimiter.Api.Services;

namespace RateLimiter.Api.Filters;

public sealed class RateLimitEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var metadata = endpoint?.Metadata.GetMetadata<RateLimitEndpointMetadata>();
        if (metadata is null || metadata.ExecutionMode != RateLimitExecutionMode.EndpointFilter)
        {
            return await next(context);
        }

        var evaluationService = context.HttpContext.RequestServices.GetRequiredService<IRateLimitEvaluationService>();
        var evaluation = await evaluationService.EvaluateAsync(context.HttpContext, metadata, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (evaluation is null)
        {
            return await next(context);
        }

        RateLimitResponseWriter.WriteHeaders(context.HttpContext.Response, evaluation.Decision);
        RateLimitResponseWriter.SetDecision(context.HttpContext, evaluation.Decision);

        if (!evaluation.Decision.IsAllowed)
        {
            return RateLimitResponseWriter.CreateProblem(context.HttpContext, evaluation.Decision, evaluation.Policy.PolicyName);
        }

        return await next(context);
    }
}
