using Arma.SharedKernel.Modules;

namespace Arma.Modules.Automation.Application;

/// <summary>Segment kurallarını düzenli aralıkla denetler.</summary>
internal sealed class SegmentRuleJob(AutomationService service) : IScheduledJob
{
    public string Name => "automation.segments";

    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public Task RunAsync(CancellationToken cancellationToken) => service.RunSegmentsAsync(cancellationToken);
}
