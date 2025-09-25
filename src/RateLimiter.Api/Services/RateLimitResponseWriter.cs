using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RateLimiter.Core.Abstractions;

namespace RateLimiter.Api.Services;

internal static class RateLimitResponseWriter
{
    private const string DecisionItemKey = "rateLimiter.decision";

    public static void SetDecision(HttpContext context, RateLimitDecision decision)
    {
        context.Items[DecisionItemKey] = decision;
    }

    public static RateLimitDecision? GetDecision(HttpContext context)
    {
        return context.Items.TryGetValue(DecisionItemKey, out var value) && value is RateLimitDecision decision
            ? decision
            : null;
    }

    public static void WriteHeaders(HttpResponse response, RateLimitDecision decision)
    {
        var counters = decision.Counters;
        response.Headers["X-RateLimit-Limit"] = counters.Limit.ToString(CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Remaining"] = counters.RemainingAsInt().ToString(CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Used"] = counters.UsedAsInt().ToString(CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Reset"] = Math.Max(1, (int)Math.Ceiling(counters.ResetAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        if (!decision.IsAllowed)
        {
            var retry = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
            response.Headers[HeaderNames.RetryAfter] = retry.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static IResult CreateProblem(HttpContext context, RateLimitDecision decision, string policyName)
    {
        var retry = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
        var problem = new ProblemDetails
        {
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Type = "https://datatracker.ietf.org/doc/html/rfc6585",
            Detail = $"Rate limit exceeded for policy '{policyName}'. Retry after {retry} second(s).",
            Instance = context.Request.Path
        };

        problem.Extensions["retryAfterSeconds"] = retry;
        problem.Extensions["limit"] = decision.Counters.Limit;
        problem.Extensions["remaining"] = decision.Counters.RemainingAsInt();

        return Results.Problem(problem);
    }
}
