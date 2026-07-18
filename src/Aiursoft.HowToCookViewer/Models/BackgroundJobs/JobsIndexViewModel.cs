using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.BackgroundJobs;

public class JobsIndexViewModel : UiStackLayoutViewModel
{
    public JobsIndexViewModel()
    {
        PageTitle = "Background Jobs";
    }

    public IReadOnlyList<RegisteredJob> RegisteredJobs { get; init; } = [];
    public IReadOnlyList<ScheduledTaskRegistration> ScheduledTasks { get; init; } = [];
    public IReadOnlyDictionary<Type, DateTime> LastRunAtByJobType { get; init; } =
        new Dictionary<Type, DateTime>();
    public IEnumerable<JobInfo> AllRecentJobs { get; init; } = [];
}
