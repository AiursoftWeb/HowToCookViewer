using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.BackgroundJobs;

/// <summary>
/// Represents a background job in the queue system.
/// </summary>
public class JobInfo
{
    [Display(Name = "Job ID")]
    public Guid JobId { get; init; } = Guid.NewGuid();

    [Display(Name = "Queue Name")]
    public required string QueueName { get; init; }

    [Display(Name = "Job Name")]
    public required string JobName { get; init; }

    [Display(Name = "Status")]
    public JobStatus Status { get; set; } = JobStatus.Pending;

    [Display(Name = "Queued at")]
    public DateTime QueuedAt { get; init; } = DateTime.UtcNow;

    [Display(Name = "Started at")]
    public DateTime? StartedAt { get; set; }

    [Display(Name = "Completed at")]
    public DateTime? CompletedAt { get; set; }

    [Display(Name = "Error message")]
    public string? ErrorMessage { get; set; }

    [Display(Name = "Service type")]
    public required Type ServiceType { get; init; }

    public required Func<object, Task> JobAction { get; init; }
}
