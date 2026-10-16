using RROMS.Models;

namespace RROMS.Services;

/// 
/// What a customer can or cannot eat, resolved once from their profile.
/// 
public class DietaryProfile
{
    /// Everything they ticked, joined with commas (for example "Vegetarian,Halal"), or free text when they chose "Other".
    public string? Preference { get; init; }

    /// The same, one entry each. "None" and empty entries are left out.
    public List<string> Preferences { get; init; } = new();

    /// Canonical allergen names (DietaryRules.AllergenNames) found in their allergy text.
    public HashSet<string> Allergens { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// Pieces of their allergy text we could not map to a known allergen - the UI says
    /// so plainly instead of pretending those were checked.
    public List<string> UnrecognisedAllergies { get; init; } = new();

    public bool HasAnyRestriction =>
        Allergens.Count > 0 || Preferences.Any(DietaryRules.PreferenceNeedsTag);
}

/// 
/// The single place that decides whether a dish is suitable for a person. The Menu filter and
/// the recommendation engine both call this, so a dish hidden from the "suits me" menu can
/// never reappear in "You might like" (brief 6.E / 6.I: never recommend a restricted dish).
///
/// Conservative by design: a dish with NO recorded allergen/diet information is not assumed
/// to be safe. If the customer has any restriction, an unrecorded dish is excluded.
/// 
public static class DietaryRules
{
    public static readonly string[] AllergenNames =
        { "Peanuts", "Tree nuts", "Milk", "Eggs", "Fish", "Shellfish", "Gluten", "Soy", "Sesame", "Sulphites" };

    public static readonly string[] DietTags =
        { "Vegan", "Halal", "Kosher", "Gluten-Free", "Dairy-Free", "Pescatarian", "No pork" };

    // Words a customer might type in "Allergies" -> the canonical allergen they mean.
    private static readonly (string Needle, string Allergen)[] Synonyms =
    {
        ("peanut", "Peanuts"), ("groundnut", "Peanuts"),
        ("tree nut", "Tree nuts"), ("almond", "Tree nuts"), ("cashew", "Tree nuts"), ("walnut", "Tree nuts"),
        ("pistachio", "Tree nuts"), ("hazelnut", "Tree nuts"), ("pecan", "Tree nuts"), ("macadamia", "Tree nuts"),
        ("milk", "Milk"), ("dairy", "Milk"), ("lactose", "Milk"), ("cheese", "Milk"),
        ("egg", "Eggs"),
        ("shellfish", "Shellfish"), ("shrimp", "Shellfish"), ("prawn", "Shellfish"), ("crab", "Shellfish"),
        ("lobster", "Shellfish"), ("crustacean", "Shellfish"), ("mollus", "Shellfish"), ("oyster", "Shellfish"),
        ("squid", "Shellfish"), ("octopus", "Shellfish"), ("clam", "Shellfish"), ("mussel", "Shellfish"),
        ("fish", "Fish"), ("salmon", "Fish"), ("tuna", "Fish"), ("anchov", "Fish"),
        ("gluten", "Gluten"), ("wheat", "Gluten"), ("coeliac", "Gluten"), ("celiac", "Gluten"),
        ("soy", "Soy"),
        ("sesame", "Sesame"),
        ("sulphite", "Sulphites"), ("sulfite", "Sulphites"),
    };

    // Plain "nut"/"nuts" on its own is ambiguous; treat it as BOTH nut allergens (the safe reading).
    private static readonly string[] AmbiguousNut = { "nut" };

    public static HashSet<string> ParseList(string? csv) =>
        new((csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    public static string JoinList(IEnumerable<string>? values, IEnumerable<string> allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        var kept = (values ?? Enumerable.Empty<string>())
            .Select(v => v.Trim())
            .Where(v => allowedSet.Contains(v))
            .Select(v => allowed.First(a => a.Equals(v, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();
        return string.Join(",", kept);
    }

    /// The diet chips on the sign-up and profile forms, in the design's order.
    public static readonly string[] NeedChips = { "Vegetarian", "Vegan", "Halal", "No pork", "Pescatarian", "Gluten-Free" };

    /// The allergen chips, the design's order first. "Milk" is shown to guests as "Dairy".
    public static readonly string[] AllergyChips = { "Peanuts", "Tree nuts", "Shellfish", "Milk", "Eggs", "Sesame", "Soy", "Fish", "Gluten", "Sulphites" };

    public static string AllergenLabel(string canonical) => canonical.Equals("Milk", StringComparison.OrdinalIgnoreCase) ? "Dairy" : canonical;

    public static string NeedLabel(string need) => need.Equals("Gluten-Free", StringComparison.OrdinalIgnoreCase) ? "Gluten-free" : need;

    /// Splits the stored allergy text into the chips that are ticked and whatever else the guest typed.
    public static (List<string> Chips, string Other) SplitAllergies(string? stored)
    {
        var chips = new List<string>();
        var other = new List<string>();
        foreach (var piece in (stored ?? string.Empty).Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var canonical = AllergyChips.FirstOrDefault(a => a.Equals(piece, StringComparison.OrdinalIgnoreCase))
                            ?? (piece.Equals("Dairy", StringComparison.OrdinalIgnoreCase) ? "Milk" : null);
            if (canonical != null) { if (!chips.Contains(canonical)) chips.Add(canonical); }
            else other.Add(piece);
        }
        return (chips, string.Join(", ", other));
    }

    /// The reverse: ticked chips (checked against the fixed list) plus the typed text, as one stored value.
    public static string? JoinAllergies(IEnumerable<string>? chips, string? other)
    {
        var kept = (chips ?? Enumerable.Empty<string>())
            .Select(c => AllergyChips.FirstOrDefault(a => a.Equals(c?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(c => c != null).Distinct().ToList();
        var text = (other ?? string.Empty).Trim();
        if (text.Length > 0) kept.Add(text.Length > 150 ? text[..150] : text);
        return kept.Count == 0 ? null : string.Join(", ", kept);
    }

    /// Ticked diet chips checked against the fixed list and stored as one comma-separated value.
    public static string? JoinNeeds(IEnumerable<string>? needs)
    {
        var kept = (needs ?? Enumerable.Empty<string>())
            .Select(n => NeedChips.FirstOrDefault(a => a.Equals(n?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(n => n != null).Distinct().ToList();
        return kept.Count == 0 ? null : string.Join(",", kept);
    }

    public static bool PreferenceNeedsTag(string? preference) =>
        !string.IsNullOrWhiteSpace(preference) &&
        (preference.Equals("Vegetarian", StringComparison.OrdinalIgnoreCase) ||
         DietTags.Contains(preference, StringComparer.OrdinalIgnoreCase));

    /// "Vegetarian, Halal" or "Vegetarian;Halal" -> two entries. "None" and blanks are dropped.
    public static List<string> ParsePreferences(string? stored) =>
        (stored ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.Equals("None", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static DietaryProfile ProfileFor(ApplicationUser? user)
    {
        var profile = new DietaryProfile { Preference = user?.DietaryPreference, Preferences = ParsePreferences(user?.DietaryPreference) };
        if (user == null || string.IsNullOrWhiteSpace(user.Allergies))
            return profile;

        var pieces = user.Allergies.Split(new[] { ',', ';', '/', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var piece in pieces)
        {
            var text = piece.ToLowerInvariant();
            var matched = false;

            foreach (var (needle, allergen) in Synonyms)
            {
                if (text.Contains(needle)) { profile.Allergens.Add(allergen); matched = true; }
            }

            if (!matched && AmbiguousNut.Any(n => text.Contains(n)))
            {
                profile.Allergens.Add("Peanuts");
                profile.Allergens.Add("Tree nuts");
                matched = true;
            }

            if (!matched) profile.UnrecognisedAllergies.Add(piece);
        }

        return profile;
    }

    /// Does the dish satisfy the customer's stated dietary preference?
    public static bool SatisfiesPreference(MenuItem dish, string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference) || !PreferenceNeedsTag(preference))
            return true; // "None", or free-text "Other" we cannot evaluate

        var tags = ParseList(dish.DietaryTags);

        if (preference.Equals("Vegetarian", StringComparison.OrdinalIgnoreCase))
            return dish.IsVegetarian || tags.Contains("Vegan");

        if (preference.Equals("Pescatarian", StringComparison.OrdinalIgnoreCase))
            return dish.IsVegetarian || tags.Contains("Vegan") || tags.Contains("Pescatarian");

        return tags.Contains(preference);
    }

    /// True when the dish is known to contain something the customer is allergic to,
    /// or when the dish has no allergen record at all and the customer has allergies.
    public static bool ConflictsWithAllergies(MenuItem dish, DietaryProfile profile)
    {
        if (profile.Allergens.Count == 0) return false;
        if (dish.Allergens == null) return true; // not recorded -> do not assume safe
        var contains = ParseList(dish.Allergens);
        return profile.Allergens.Any(contains.Contains);
    }

    public static bool IsSuitable(MenuItem dish, DietaryProfile profile) =>
        profile.Preferences.All(p => SatisfiesPreference(dish, p)) && !ConflictsWithAllergies(dish, profile);
}
