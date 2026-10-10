using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// A guest who asked to be told if a full time slot opens up.
public class WaitlistEntry
{
    public int Id { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    public DateTime WantedDateTime { get; set; }

    [Range(1, 30)]
    public int PartySize { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// When we told the guest a table might be free. Null while still waiting.
    public DateTime? NotifiedAt { get; set; }
}
