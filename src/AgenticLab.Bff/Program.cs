using AgenticLab.Bff.Endpoints;
using AgenticLab.Bff.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddFrontendProxy();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapDefaultEndpoints();
app.MapFrontendEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.Run();