using IzziWebApp.Hubs;
using IzziWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<SimulationService>();

var app = builder.Build();

// Pre-instantiate so the singleton is ready before any client connects.
app.Services.GetRequiredService<SimulationService>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimulationHub>("/simhub");

app.Run();
