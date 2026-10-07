using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// <summary>
/// One row per day of the week. Seeded automatically for all seven days.
/// </summary>
public class OperatingHours
{
    public int Id { get; set; }

    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    public TimeSpan OpenTime { get; set; } = new TimeSpan(11, 0, 0);

    public TimeSpan CloseTime { get; set; } = new TimeSpan(22, 0, 0);

    public bool IsClosed { get; set; } = false;
}
