using ThreeOfSpades.Api.Services;

namespace ThreeOfSpades.Api.Hosting;

public class DisconnectWorker(LiveGameService games) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await games.TickDisconnects();
            }
            catch
            {
                // keep the loop alive
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
