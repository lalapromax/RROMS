namespace RROMS.ViewModels;

/// Inputs for the dietary needs and allergy chips (sign-up and profile). Names are the form field names to post back. Sub: the headings sit under a section heading, so they are h3.
public record DietaryChipsVM(string NeedsName, string AllergiesName, string OtherName, IReadOnlyCollection<string> Needs, IReadOnlyCollection<string> Allergies, string? Other, string IdPrefix = "diet", bool Sub = false);
