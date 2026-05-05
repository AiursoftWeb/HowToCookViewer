using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.BackgroundJobs;

/// <summary>
/// Represents the status of a background job.
/// </summary>
public enum JobStatus
{
    [Display(Name = "Pending")]
    Pending,
    [Display(Name = "Processing")]
    Processing,
    [Display(Name = "Success")]
    Success,
    [Display(Name = "Failed")]
    Failed,
    [Display(Name = "Cancelled")]
    Cancelled
}
