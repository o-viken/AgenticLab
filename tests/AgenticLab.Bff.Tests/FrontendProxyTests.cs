using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.Bff.Endpoints;
using AgenticLab.Bff.Startup;
using Xunit;

namespace AgenticLab.Bff.Tests;

/// <summary>Checks the real HTTP streaming boundary, including the first manually released backend step.</summary>
public sealed class FrontendProxyTests
{
    [Fact]
    public async Task ManualStreamAcceptsControlBeforeHeadersAndPropagatesDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var session = new FlowSession("manual", true, 0);
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissions = 0;
        string? forwardedBody = null;
        await using var upstream = CreateBuilder().Build();
        upstream.MapPost("/chat/stream", async context =>
        {
            submissions++;
            forwardedBody = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
            submitted.TrySetResult();
            try
            {
                await TypedResults.ServerSentEvents(Events(context.RequestAborted), "flow").ExecuteAsync(context);
            }
            catch (OperationCanceledException) { }
            finally { disconnected.TrySetResult(); }
        });
        upstream.MapPost("/chat/control", () => { session.Advance(); return TypedResults.NoContent(); });
        await upstream.StartAsync(timeout.Token);
        await using var bff = CreateProxy(upstream);
        await bff.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(bff.Urls)) };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        const string body = "{\"message\":\"hello\",\"manual\":true,\"sessionId\":\"manual\"}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        var pendingResponse = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        await submitted.Task.WaitAsync(timeout.Token);
        Assert.False(pendingResponse.IsCompleted);
        using var control = await client.PostAsJsonAsync("/api/chat/control", new { sessionId = "manual", action = "next" }, timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, control.StatusCode);
        using var response = await pendingResponse.WaitAsync(timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        string? line;
        do { line = await reader.ReadLineAsync(timeout.Token); } while (line is not null && !line.StartsWith("data:"));
        Assert.Contains("\"kind\":\"received\"", line);
        Assert.Equal(body, forwardedBody);
        Assert.Equal(1, submissions);
        cancellation.Cancel();
        response.Dispose();
        await disconnected.Task.WaitAsync(timeout.Token);

        async IAsyncEnumerable<FlowEvent> Events([EnumeratorCancellation] CancellationToken token)
        {
            await session.WaitForStepAsync(token);
            yield return new FlowEvent(1, "received", "Message received");
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    [Fact]
    public async Task OnlyAllowedMethodsAndPathsForwardAndUpstreamErrorsSurvive()
    {
        await using var upstream = CreateBuilder().Build();
        upstream.MapGet("/agents", () => TypedResults.Json(new { agents = Array.Empty<object>(), @default = "" }));
        upstream.MapPost("/chat/reset", () => TypedResults.BadRequest("invalid conversation"));
        await upstream.StartAsync();
        await using var bff = CreateProxy(upstream);
        bff.MapFallback(() => TypedResults.Text("frontend index"));
        bff.MapOpenApi();
        await bff.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(bff.Urls)) };
        using var agents = await client.GetAsync("/api/agents");
        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);
        Assert.Contains("\"agents\":[]", await agents.Content.ReadAsStringAsync());
        using var unknown = await client.GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain("frontend index", await unknown.Content.ReadAsStringAsync());
        using var wrongMethod = await client.GetAsync("/api/chat/stream");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        using var invalidType = await client.PostAsync("/api/chat/reset", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, invalidType.StatusCode);
        using var reset = await client.PostAsJsonAsync("/api/chat/reset", new { conversationId = "" });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal("\"invalid conversation\"", await reset.Content.ReadAsStringAsync());
        using var specification = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.Equal(5, specification.RootElement.GetProperty("paths").EnumerateObject().Count());
        Assert.True(specification.RootElement.GetProperty("paths").TryGetProperty("/api/chat/stream", out _));
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        return builder;
    }

    private static WebApplication CreateProxy(WebApplication upstream)
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["services:aiservice:http:0"] = Assert.Single(upstream.Urls)
        });
        builder.AddFrontendProxy();
        var app = builder.Build();
        app.MapFrontendEndpoints();
        return app;
    }
}