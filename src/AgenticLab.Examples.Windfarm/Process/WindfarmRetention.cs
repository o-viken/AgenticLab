using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AgenticLab.Examples.Windfarm.Process;

internal sealed class WindfarmRetention(IWindfarmProcess process, IOptions<WindfarmOptions> options, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken)) process.Cleanup();
    }
}