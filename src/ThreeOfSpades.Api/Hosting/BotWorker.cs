using ThreeOfSpades.Api.Services;

namespace ThreeOfSpades.Api.Hosting;

public class BotWorker(LiveGameService games) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await games.TickBots();
            }
            catch
            {
                // keep the loop alive
            }
            await Task.Delay(TimeSpan.FromMilliseconds(400), stoppingToken);
        }
    }
}
