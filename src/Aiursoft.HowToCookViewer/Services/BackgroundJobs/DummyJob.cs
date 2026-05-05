using Aiursoft.Canon.BackgroundJobs;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// A dummy background job used to demonstrate and test the background job framework.
/// Simulates a realistic work unit by sleeping for a random duration and occasionally
/// failing (10% chance) to verify error reporting in the admin UI.
/// </summary>
public class DummyJob(ILogger<DummyJob> logger) : IBackgroundJob
{
    public string Name => "Dummy Job";

    public string Description =>
        "Simulates work for 5–15 seconds with a 10% random failure rate. " +
        "Use this to verify that the background job framework is working correctly.";

    public async Task ExecuteAsync()
    {
        var sleepSeconds = Random.Shared.Next(5, 15);
        logger.LogInformation(
            "DummyJob started — simulating work for {Seconds} seconds.", sleepSeconds);

        await Task.Delay(TimeSpan.FromSeconds(sleepSeconds));

        // 10% chance of failure, to demonstrate error reporting.
        if (Random.Shared.Next(0, 100) < 10)
        {
            throw new InvalidOperationException(
                "DummyJob intentionally failed (10% probability — this is expected).");
        }

        logger.LogInformation("DummyJob completed successfully.");
    }
}
