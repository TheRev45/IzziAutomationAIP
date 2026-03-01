using IzziWebApp.Services;
using Microsoft.AspNetCore.SignalR;

namespace IzziWebApp.Hubs;

public class SimulationHub : Hub
{
    private readonly SimulationService _sim;

    public SimulationHub(SimulationService sim)
    {
        _sim = sim;
    }

    public async Task Start()   => await _sim.StartAsync();
    public async Task Pause()   => await _sim.PauseAsync();
    public async Task Resume()  => await _sim.ResumeAsync();
    public async Task Reset()   => await _sim.ResetAsync();

    public async Task SetSpeed(double multiplier)
    {
        _sim.SetSpeed(multiplier);
        await _sim.BroadcastStateForce();
    }

    /// <summary>Sends current state to the newly connected client.</summary>
    public override async Task OnConnectedAsync()
    {
        await _sim.BroadcastStateToClient(Clients.Caller);
        await base.OnConnectedAsync();
    }
}
