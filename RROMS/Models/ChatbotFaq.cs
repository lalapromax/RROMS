using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;
public class ChatbotFaq
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Question { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Answer { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string Keywords { get; set; } = string.Empty;

    [StringLength(50)]
    public string Category { get; set; } = "General";

    public bool IsActive { get; set; } = true;
}
