using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinyFileIO.Models.Entities;

[Table("background_job_runs")]
public sealed class BackgroundJobRun
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    [Column("job_type")]
    public string JobType { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    [Column("status")]
    public string Status { get; set; } = BackgroundJobStatuses.Queued;

    [Column("created_utc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [Column("started_utc")]
    public DateTime? StartedUtc { get; set; }

    [Column("finished_utc")]
    public DateTime? FinishedUtc { get; set; }

    [MaxLength(128)]
    [Column("created_by_user_id")]
    public string? CreatedByUserId { get; set; }

    [MaxLength(256)]
    [Column("created_by_username")]
    public string? CreatedByUsername { get; set; }

    [MaxLength(63)]
    [Column("target_bucket")]
    public string? TargetBucket { get; set; }

    [Required]
    [Column("parameters_json")]
    public string ParametersJson { get; set; } = "{}";

    [Column("error")]
    public string? Error { get; set; }

    [Column("total_items")]
    public int TotalItems { get; set; }

    [Column("processed_items")]
    public int ProcessedItems { get; set; }

    [Column("succeeded_items")]
    public int SucceededItems { get; set; }

    [Column("skipped_items")]
    public int SkippedItems { get; set; }

    [Column("failed_items")]
    public int FailedItems { get; set; }

    [Column("bytes_processed")]
    public long BytesProcessed { get; set; }

    [Column("stats_json")]
    public string? StatsJson { get; set; }
}

public static class BackgroundJobStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}
