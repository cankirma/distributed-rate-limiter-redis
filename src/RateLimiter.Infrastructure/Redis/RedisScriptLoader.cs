using System.IO;
using System.Reflection;
using System.Text;

namespace RateLimiter.Infrastructure.Redis;

internal static class RedisScriptLoader
{
    private static readonly Assembly Assembly = typeof(RedisScriptLoader).Assembly;

    public static string LoadScript(string scriptName)
    {
        var resourceName = $"RateLimiter.Infrastructure.Redis.Scripts.{scriptName}";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Redis script '{resourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
