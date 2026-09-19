namespace TheSeries.Bff.Startup;

/// <summary>Registers a streaming forwarder without the retrying HttpClient pipeline used by ordinary service calls.</summary>
public static class ServiceRegistration
{
    /// <summary>The BFF uses service discovery and never owns model credentials or agent execution.</summary>
    public static WebApplicationBuilder AddFrontendProxy(this WebApplicationBuilder builder)
    {
        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpForwarderWithServiceDiscovery();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        return builder;
    }
}