using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

public enum SetMenuStatus
{
    Draft = 0,
    Active = 1,
    Hidden = 2
}

/// 
/// A menu the whole table can choose: a name, one price per guest, and courses in serving order (soup, appetiser, main, drink,
/// or whatever the restaurant decides). In each course the guest picks from a short list of dishes. Staff and administrators
/// can both create and edit set menus; only Active ones are shown to guests.
/// 
public class SetMenu
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    /// The one-line summary on cards, for example "Soup, appetiser, main and a glass".
    [StringLength(120)]
    public string? Tagline { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 100000)]
    public decimal PricePerGuest { get; set; }

    /// Optional photo. Without one, guests see the plate picture of the first course's first dish.
    [StringLength(300)]
    public string? ImageUrl { get; set; }

    public SetMenuStatus Status { get; set; } = SetMenuStatus.Draft;

    /// Days it is offered, as a bit mask: Sunday = 1, Monday = 2, Tuesday = 4 ... Saturday = 64. 127 means every day.
    public int DaysMask { get; set; } = 127;

    public bool ServedAtLunch { get; set; }
    public bool ServedAtDinner { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedById { get; set; }

    public ICollection<SetMenuCourse> Courses { get; set; } = new List<SetMenuCourse>();

    public bool OffersOn(DayOfWeek day) => (DaysMask & (1 << (int)day)) != 0;

    public static int MaskOf(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (m, d) => m | (1 << (int)d));
}

/// One course of a set menu. <see cref="Picks"/> is how many dishes a guest takes from it (0 = all of them).
public class SetMenuCourse
{
    public int Id { get; set; }

    public int SetMenuId { get; set; }
    public SetMenu? SetMenu { get; set; }

    [Required, StringLength(40)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 5)]
    public int Picks { get; set; } = 1;

    public int SortOrder { get; set; }

    public ICollection<SetMenuDish> Dishes { get; set; } = new List<SetMenuDish>();
}

/// A dish offered in a course, with an optional extra charge when the guest picks it (for example the sole, + RM 22).
public class SetMenuDish
{
    public int Id { get; set; }

    public int SetMenuCourseId { get; set; }
    public SetMenuCourse? Course { get; set; }

    public int MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 10000)]
    public decimal Supplement { get; set; }

    public int SortOrder { get; set; }
}
