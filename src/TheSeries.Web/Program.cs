using TheSeries.Web;
using TheSeries.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Talks to the AI service. Under Aspire orchestration the name resolves via service discovery;
// when run standalone, override with AiService:Url (e.g. https://localhost:7123).
var serviceUrl = builder.Configuration["AiService:Url"] ?? "https+http://aiservice";
builder.Services.AddHttpClient<AiServiceClient>(client => client.BaseAddress = new Uri(serviceUrl));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
