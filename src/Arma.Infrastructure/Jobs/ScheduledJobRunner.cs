using Arma.SharedKernel.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arma.Infrastructure.Jobs;

/// <summary>
/// Modüllerin <see cref="IScheduledJob"/> işlerini dakikada bir yoklar ve süresi gelenleri sırayla çalıştırır.
/// "Arma:Jobs:Enabled" false ise (testler, birden çok sunucuda yalnızca biri dışında) hiç çalışmaz.
/// </summary>
public sealed class ScheduledJobRunner(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<ScheduledJobRunner> logger)
    : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, DateTimeOffset> lastRun = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Arma:Jobs:Enabled", true))
        {
            return;
        }

        // Uygulama açılışı ve şema güncellemesi bitsin.
        await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken);
        using var timer = new PeriodicTimer(Tick, clock);
        do
        {
            await RunDueJobsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunDueJobsAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            var now = clock.GetUtcNow();
            if (lastRun.TryGetValue(job.Name, out var last) && now - last < job.Interval)
            {
                continue;
            }

            lastRun[job.Name] = now;
            try
            {
                await using var jobScope = scopes.CreateAsyncScope();
                var current = jobScope.ServiceProvider.GetServices<IScheduledJob>().First(j => j.Name == job.Name);
                await current.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Zamanlanmış iş başarısız oldu: {Job}", job.Name);
            }
        }
    }
}
