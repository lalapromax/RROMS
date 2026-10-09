using System.ComponentModel.DataAnnotations;

namespace RROMS.ViewModels;

public class ReservationFormVM
{
    public int? ReservationId { get; set; }

    [Required(ErrorMessage = "Please choose a date.")]
    [DataType(DataType.Date)]
    public DateTime ReservationDate { get; set; } = DateTime.Today.AddDays(1);

    [Required(ErrorMessage = "Please choose a time.")]
    public string ReservationTime { get; set; } = "19:00";

    [Required, Range(1, 30, ErrorMessage = "Party size must be between 1 and 30.")]
    public int PartySize { get; set; } = 2;

    public int? DiningAreaId { get; set; }

    [StringLength(400)]
    public string? SpecialRequests { get; set; }

    public int? SelectedTableId { get; set; }
}
