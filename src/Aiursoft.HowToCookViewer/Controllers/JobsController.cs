using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;
using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Models.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.HowToCookViewer.Services;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// Controller for the background job administration UI at <c>/Jobs</c>.
/// Displays all registered background jobs and their recent execution history.
/// Allows administrators to manually trigger any registered job.
/// </summary>
[Authorize]
[LimitPerMin]
public class JobsController(
    ServiceTaskQueue taskQueue,
    BackgroundJobRegistry jobRegistry,
    IEnumerable<ScheduledTaskRegistration> scheduledTasks) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanViewBackgroundJobs)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "System",
        CascadedLinksIcon = "cog",
        CascadedLinksOrder = 9999,
        LinkText = "Background Jobs",
        LinkOrder = 2)]
    public IActionResult Index()
    {
        var oneHourAgo = TimeSpan.FromHours(1);
        var recentCompleted = taskQueue.GetRecentCompletedTasks(oneHourAgo).Select(ToJobInfo);
        var pending = taskQueue.GetPendingTasks().Select(ToJobInfo);
        var processing = taskQueue.GetProcessingTasks().Select(ToJobInfo);

        var allJobs = pending
            .Concat(processing)
            .Concat(recentCompleted)
            .OrderByDescending(j => j.QueuedAt)
            .ToList();

        var lastRunAtByJobType = taskQueue.GetAllTasks()
            .Select(task => new
            {
                task.ServiceType,
                LastRunAt = task.CompletedAt ?? task.StartedAt
            })
            .Where(x => x.LastRunAt.HasValue)
            .GroupBy(x => x.ServiceType)
            .ToDictionary(
                g => g.Key,
                g => g.Max(x => x.LastRunAt!.Value));

        var viewModel = new JobsIndexViewModel
        {
            RegisteredJobs = jobRegistry.GetAll(),
            ScheduledTasks = scheduledTasks
                .OrderBy(t => t.JobType.Name)
                .ToList(),
            LastRunAtByJobType = lastRunAtByJobType,
            AllRecentJobs = allJobs
        };

        return this.StackView(viewModel);
    }

    /// <summary>
    /// Manually triggers an immediate, one-off run of the background job identified
    /// by <paramref name="jobTypeName"/>. This is a fire-and-forget enqueue — the
    /// response redirects immediately while the job runs in the background.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanViewBackgroundJobs)]
    [ValidateAntiForgeryToken]
    public IActionResult Trigger(string jobTypeName)
    {
        jobRegistry.TriggerNow(jobTypeName);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Cancels a pending (not yet started) job.</summary>
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanViewBackgroundJobs)]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(Guid jobId)
    {
        taskQueue.CancelTask(jobId);
        return RedirectToAction(nameof(Index));
    }

    private static JobInfo ToJobInfo(TaskExecutionInfo task)
    {
        return new JobInfo
        {
            JobId = task.TaskId,
            QueueName = task.QueueName,
            JobName = task.TaskName,
            Status = task.Status switch
            {
                TaskExecutionStatus.Pending => JobStatus.Pending,
                TaskExecutionStatus.Processing => JobStatus.Processing,
                TaskExecutionStatus.Success => JobStatus.Success,
                TaskExecutionStatus.Failed => JobStatus.Failed,
                TaskExecutionStatus.Cancelled => JobStatus.Cancelled,
                _ => JobStatus.Pending
            },
            QueuedAt = task.QueuedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            ErrorMessage = task.ErrorMessage,
            ServiceType = task.ServiceType,
            JobAction = task.TaskAction
        };
    }
}
