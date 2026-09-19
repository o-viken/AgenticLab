using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Yarp.ReverseProxy.Forwarder;

namespace TheSeries.Bff.Endpoints;

/// <summary>Forwards only the existing API surface needed by the React prototype, preserving JSON and SSE bodies.</summary>
public static class FrontendEndpoints
{
    private static readonly (string Path, string Method, string Summary)[] Routes =
    [
        ("/agents", "GET", "List registered agents and their capabilities."),
        ("/vendors", "GET", "List vendor harnesses and supported agent modes."),
        ("/chat/stream", "POST", "Stream a run using the existing FlowChatRequest and flow SSE events."),
        ("/chat/control", "POST", "Step, pause, resume, stop or answer the identified run."),
        ("/chat/reset", "POST", "Reset the identified conversation without submitting a new message.")
    ];

    /// <summary>Maps fixed destinations; API misses never fall through to the frontend's index document.</summary>
    public static WebApplication MapFrontendEndpoints(this WebApplication app)
    {
        var destination = app.Configuration["AiService:Url"] ?? "https+http://aiservice";
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", out var path))
            {
                var route = Routes.FirstOrDefault(route => path.Equals(route.Path, StringComparison.OrdinalIgnoreCase));
                if (route.Path is null)
                {
                    await TypedResults.Problem(statusCode: 404, title: "Unknown API route").ExecuteAsync(context);
                    return;
                }
                if (context.Request.Method != route.Method)
                {
                    context.Response.Headers.Allow = route.Method;
                    await TypedResults.Problem(statusCode: 405, title: "Method not allowed").ExecuteAsync(context);
                    return;
                }
                if (route.Method == "POST" && !context.Request.HasJsonContentType())
                {
                    await TypedResults.Problem(statusCode: 415, title: "JSON content is required").ExecuteAsync(context);
                    return;
                }
                if (route.Path == "/chat/stream"
                    && context.Features.Get<IHttpMinResponseDataRateFeature>() is { } rate)
                    rate.MinDataRate = null;
            }
            await next(context);
        });

        foreach (var route in Routes)
        {
            var streaming = route.Path == "/chat/stream";
            app.MapForwarder("/api" + route.Path, destination, new ForwarderRequestConfig
            {
                ActivityTimeout = streaming ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30),
                AllowResponseBuffering = false
            }, route.Path)
                .WithMetadata(new HttpMethodMetadata([route.Method]),
                    new ProducesResponseTypeMetadata(streaming || route.Method == "GET" ? 200 : 204,
                        typeof(JsonElement), [streaming ? "text/event-stream" : "application/json"]))
                .WithSummary(route.Summary)
                .WithTags("Frontend proxy")
                .Add(endpoint => endpoint.Metadata.Add(endpoint.RequestDelegate!.Method));
        }
        return app;
    }
}