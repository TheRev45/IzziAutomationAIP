using IzziWebApp.Hubs;
using IzziWebApp.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton(sp =>
{
    var hub  = sp.GetRequiredService<IHubContext<SimulationHub>>();
    var env  = sp.GetRequiredService<IWebHostEnvironment>();
    var path = Path.Combine(env.ContentRootPath, "..", "TestData", "Blue Prism");
    return new SimulationService(hub, path);
});

var app = builder.Build();

// Pre-instantiate so the singleton is ready before any client connects.
app.Services.GetRequiredService<SimulationService>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimulationHub>("/simhub");

app.Run();
