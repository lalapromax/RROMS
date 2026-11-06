using System.Text.RegularExpressions;
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

    // ------------------------------------------------------------------------------------------------
    // The strict check. A dish is suitable only when its record SAYS so (flags, diet labels, allergen list) and its own
    // wording does not contradict that. Anything that cannot be confirmed counts as unsuitable: for a guest with a diet or
    // an allergy, "not sure" never means "safe".
    // ------------------------------------------------------------------------------------------------

    private const RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static Regex Words(string alternatives) => new($@"\b(?:{alternatives})(?:s|es)?\b", Rx);

    // What a dish's name or description gives away. English plus the French used on the menu.
    private static readonly Regex MeatWords = Words("beef|b[oœ]e?uf|steak|veal|veau|pork|porc|ham|jambon|bacon|lard|lardons?|pancetta|prosciutto|chorizo|salami|sausage|saucisse|boudin|lamb|agneau|mutton|chicken|poulet|poularde|turkey|dinde|duck|canard|magret|goose|oie|quail|caille|pheasant|faisan|venison|rabbit|lapin|meat|viande|foie gras|oxtail|short rib|brisket|tripe|gelatin|gelatine|bone marrow|escargots?|snails?|game|hen");
    private static readonly Regex FishWords = Words("fish|poisson|salmon|saumon|tuna|thon|cod|morue|sea bass|bass|trout|truite|halibut|turbot|sole|sardine|mackerel|maquereau|anchov\\w*|caviar|roe|eel|anguille|monkfish|lotte|haddock|herring|hareng|skate|raie|snapper|swordfish|dorade|bream|john dory|saint-pierre|seafood|fruits de mer");
    private static readonly Regex ShellfishWords = Words("shellfish|crustaceans?|prawn|shrimp|crevette|crab|crabe|lobster|homard|langoustine|langouste|scallop|coquilles? saint-jacques|oyster|hu[iî]tre|mussel|moule|clam|palourde|squid|calamari|octopus|poulpe|cuttlefish");
    private static readonly Regex PorkWords = Words("pork|porc|ham|jambon|bacon|lard|lardons?|pancetta|prosciutto|chorizo|salami|sausage|saucisse|boudin|guanciale|speck|gammon|trotter");
    private static readonly Regex DairyEggWords = Words("milk|lait|cream|cr[eè]me|butter|beurre|cheese|fromage|ch[eè]vre|yoghurt|yogurt|custard|egg|oeuf|œuf|yolk|honey|miel|mayonnaise|b[ée]chamel|mascarpone|ricotta|parmesan|gruy[eè]re|brie|camembert");

    // Allergen -> words in a dish's wording that point to it (checked as well as the dish's allergen list).
    private static readonly Dictionary<string, Regex> AllergenWordsMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Peanuts"] = Words("peanut|groundnut|arachide"),
        ["Tree nuts"] = Words("nut|almond|amande|cashew|walnut|noix|pistachio|pistache|hazelnut|noisette|pecan|macadamia|praline|marzipan|frangipane|nougat"),
        ["Milk"] = Words("milk|lait|cream|cr[eè]me|butter|beurre|cheese|fromage|ch[eè]vre|yoghurt|yogurt|custard|b[ée]chamel|mascarpone|ricotta|parmesan|gruy[eè]re|brie|camembert|ghee|whey"),
        ["Eggs"] = Words("egg|oeuf|œuf|yolk|mayonnaise|aioli|meringue|custard|hollandaise|b[ée]arnaise"),
        ["Fish"] = FishWords,
        ["Shellfish"] = ShellfishWords,
        ["Gluten"] = Words("wheat|bl[eé]|flour|farine|bread|pain|brioche|pasta|gnocchi|noodle|couscous|semolina|barley|rye|cro[uû]ton|pastry|breadcrumb|panko|tempura|batter"),
        ["Soy"] = Words("soy|soya|soja|tofu|edamame|miso|tamari|shoyu"),
        ["Sesame"] = Words("sesame|s[eé]same|tahini"),
        ["Sulphites"] = Words("wine|vin|vinegar|vinaigre|champagne|prosecco|cider|cidre|sherry|port|dried fruit|dried apricot"),
    };

    /// Does the dish's own wording mention something that contains this allergen?
    public static bool WordingMentions(string allergen, string? text) =>
        !string.IsNullOrWhiteSpace(text) && AllergenWordsMap.TryGetValue(allergen, out var rx) && rx.IsMatch(text);

    public readonly record struct DishCheck(bool Suitable, string? Why);

    /// The one decision: may this dish be put in front of this guest? When it may not, Why says in plain words (for staff and logs).
    public static DishCheck Check(MenuItem dish, DietaryProfile profile)
    {
        var tags = ParseList(dish.DietaryTags);
        var wording = $"{dish.Name}. {dish.Description}";

        foreach (var preference in profile.Preferences)
        {
            if (!PreferenceNeedsTag(preference)) continue;
            var problem = PreferenceProblem(dish, tags, wording, preference);
            if (problem != null) return new DishCheck(false, problem);
        }

        if (profile.Allergens.Count > 0)
        {
            if (dish.Allergens == null) return new DishCheck(false, "its allergens are not recorded");
            var listed = ParseList(dish.Allergens);
            foreach (var allergen in profile.Allergens)
            {
                if (listed.Contains(allergen)) return new DishCheck(false, $"it contains {AllergenLabel(allergen).ToLowerInvariant()}");
                if (WordingMentions(allergen, wording)) return new DishCheck(false, $"its description points to {AllergenLabel(allergen).ToLowerInvariant()}");
            }
        }

        return new DishCheck(true, null);
    }

    private static string? PreferenceProblem(MenuItem dish, HashSet<string> tags, string wording, string preference)
    {
        var vegan = tags.Contains("Vegan");
        var meaty = MeatWords.IsMatch(wording);
        var fishy = FishWords.IsMatch(wording) || ShellfishWords.IsMatch(wording);

        switch (preference.Trim().ToLowerInvariant())
        {
            case "vegetarian":
                if (!(dish.IsVegetarian || vegan)) return "it is not marked vegetarian";
                if (meaty || fishy) return "its description names meat or fish";
                return null;
            case "vegan":
                if (!vegan) return "it is not marked vegan";
                if (meaty || fishy || DairyEggWords.IsMatch(wording)) return "its description names an animal product";
                return null;
            case "pescatarian":
                if (!(dish.IsVegetarian || vegan || tags.Contains("Pescatarian"))) return "it is not marked vegetarian or pescatarian";
                if (meaty) return "its description names meat";
                return null;
            case "halal":
                if (!tags.Contains("Halal")) return "it is not marked halal";
                if (PorkWords.IsMatch(wording)) return "its description names pork";
                return null;
            case "no pork":
                if (!(tags.Contains("No pork") || tags.Contains("Halal") || dish.IsVegetarian || vegan)) return "it is not marked pork-free";
                if (PorkWords.IsMatch(wording)) return "its description names pork";
                return null;
            case "gluten-free":
                if (!tags.Contains("Gluten-Free")) return "it is not marked gluten-free";
                if (dish.Allergens == null || ParseList(dish.Allergens).Contains("Gluten")) return "its gluten information does not confirm it";
                return null;
            case "dairy-free":
                if (!tags.Contains("Dairy-Free")) return "it is not marked dairy-free";
                if (dish.Allergens == null || ParseList(dish.Allergens).Contains("Milk")) return "its dairy information does not confirm it";
                return null;
            default:
                return tags.Contains(preference.Trim()) ? null : $"it is not marked {preference.Trim().ToLowerInvariant()}";
        }
    }

    public static bool IsSuitable(MenuItem dish, DietaryProfile profile) => Check(dish, profile).Suitable;

    /// Does a piece of free text (a model's reply) talk about something this guest avoids? Used to throw such replies away.
    public static bool MentionsForbidden(string? text, DietaryProfile profile)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var preference in profile.Preferences)
        {
            switch (preference.Trim().ToLowerInvariant())
            {
                case "vegetarian":
                    if (MeatWords.IsMatch(text) || FishWords.IsMatch(text) || ShellfishWords.IsMatch(text)) return true;
                    break;
                case "vegan":
                    if (MeatWords.IsMatch(text) || FishWords.IsMatch(text) || ShellfishWords.IsMatch(text) || DairyEggWords.IsMatch(text)) return true;
                    break;
                case "pescatarian":
                    if (MeatWords.IsMatch(text)) return true;
                    break;
                case "halal":
                case "no pork":
                    if (PorkWords.IsMatch(text)) return true;
                    break;
            }
        }
        return profile.Allergens.Any(a => WordingMentions(a, text));
    }

    // ------------------------------------------------------------------------------------------------
    // What a guest says in the chat ("I'm vegetarian", "allergic to prawns") counts like their saved profile.
    // ------------------------------------------------------------------------------------------------

    private static readonly (Regex Pattern, string Preference)[] SaidPreferences =
    {
        (new Regex(@"\bvegetarians?\b|\bveggie\b", Rx), "Vegetarian"),
        (new Regex(@"\bvegans?\b", Rx), "Vegan"),
        (new Regex(@"\bpesc[ae]tarians?\b", Rx), "Pescatarian"),
        (new Regex(@"\bhalal\b", Rx), "Halal"),
        (new Regex(@"\bkosher\b", Rx), "Kosher"),
        (new Regex(@"\bpork[- ]free\b|\b(?:no|without|avoid|avoiding)\s+(?:any\s+)?(?:pork|bacon|ham)\b|\b(?:cannot|can't|cant|can not|don't|dont|do not)\s+eat\s+(?:any\s+)?(?:pork|bacon|ham)\b", Rx), "No pork"),
        (new Regex(@"\bgluten[- ]?free\b|\bcoeliacs?\b|\bceliacs?\b", Rx), "Gluten-Free"),
        (new Regex(@"\bdairy[- ]?free\b|\blactose[- ]intoleran\w*\b|\b(?:no|without)\s+dairy\b", Rx), "Dairy-Free"),
    };

    // "not vegetarian", "non-vegan": the guest is saying what they are NOT.
    private static readonly Regex NotThat = new(@"\b(?:non|not|isn't|isnt|aren't|arent)[- ]*(?:a\s+)?(?:vegetarian|vegan|halal|kosher|pesc[ae]tarian)s?\b", Rx);

    private static readonly Regex StrongCue = new(@"\b(?:allergic to|allergy to|allergies to|allergies include|allergic|intoleran\w* to|intoleran\w*)\b", Rx);
    private static readonly Regex WeakCue = new(@"\b(?:without|avoid|avoiding|free of|no|cannot eat|can't eat|cant eat|can not eat|don't eat|dont eat|do not eat|not eating)\b", Rx);
    private static readonly Regex AllergyBefore = new(@"\b([a-z]+(?: [a-z]+)?)\s+allerg(?:y|ies|ic)\b", Rx);
    private static readonly HashSet<string> Stoppers = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "which", "how", "do", "does", "can", "could", "would", "please", "thanks", "thank", "but", "that", "is", "are", "with",
        "for", "show", "give", "any", "dishes", "dish", "meals", "meal", "food", "options", "option", "so", "because", "when", "i", "we", "you"
    };

    /// The diet and allergies a guest has put into words in these messages (the chat keeps what was said earlier too).
    public static DietaryProfile ProfileFromText(IEnumerable<string> messages)
    {
        var preferences = new List<string>();
        var allergens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var original in messages)
        {
            var text = NotThat.Replace((original ?? string.Empty).ToLowerInvariant().Replace('’', '\''), " ");

            foreach (var (pattern, preference) in SaidPreferences)
                if (pattern.IsMatch(text) && !preferences.Contains(preference, StringComparer.OrdinalIgnoreCase))
                    preferences.Add(preference);

            // "allergic to peanuts and shellfish", "intolerant to dairy": a longer stretch of words follows the cue.
            foreach (Match m in StrongCue.Matches(text))
                foreach (var a in AllergensIn(WordsAfter(text, m.Index + m.Length, 12, stopAtStoppers: false))) allergens.Add(a);

            // "no nuts", "without prawns", "can't eat shellfish": only the next few words, and not past a question word.
            foreach (Match m in WeakCue.Matches(text))
                foreach (var a in AllergensIn(WordsAfter(text, m.Index + m.Length, 5, stopAtStoppers: true))) allergens.Add(a);

            // "a peanut allergy", "shellfish allergies"
            foreach (Match m in AllergyBefore.Matches(text))
                foreach (var a in AllergensIn(m.Groups[1].Value)) allergens.Add(a);
        }

        // Gluten-free and dairy-free are diets and allergens at once; treat them as both.
        if (preferences.Contains("Gluten-Free", StringComparer.OrdinalIgnoreCase)) allergens.Add("Gluten");
        if (preferences.Contains("Dairy-Free", StringComparer.OrdinalIgnoreCase)) allergens.Add("Milk");

        return new DietaryProfile { Preference = string.Join(",", preferences), Preferences = preferences, Allergens = allergens };
    }

    private static string WordsAfter(string text, int from, int max, bool stopAtStoppers)
    {
        var tail = text[from..];
        var end = tail.IndexOfAny(new[] { '.', '!', '?', ';', '\n' });
        if (end >= 0) tail = tail[..end];
        var kept = new List<string>();
        foreach (var raw in tail.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (kept.Count >= max) break;
            if (stopAtStoppers && Stoppers.Contains(raw)) break;
            if (!stopAtStoppers && raw is "but") break;
            kept.Add(raw);
        }
        return string.Join(" ", kept);
    }

    /// The canonical allergens a piece of text refers to, using the same word list as the saved-profile reader.
    public static HashSet<string> AllergensIn(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lower = text.ToLowerInvariant();
        var matched = false;
        foreach (var (needle, allergen) in Synonyms)
            if (lower.Contains(needle)) { found.Add(allergen); matched = true; }
        if (!matched && AmbiguousNut.Any(n => lower.Contains(n))) { found.Add("Peanuts"); found.Add("Tree nuts"); }
        return found;
    }

    /// Both profiles at once: every preference and allergen from either one. The stricter reading always wins.
    public static DietaryProfile Merge(DietaryProfile a, DietaryProfile b)
    {
        var preferences = a.Preferences.Concat(b.Preferences).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var allergens = new HashSet<string>(a.Allergens.Concat(b.Allergens), StringComparer.OrdinalIgnoreCase);
        return new DietaryProfile
        {
            Preference = string.Join(",", preferences),
            Preferences = preferences,
            Allergens = allergens,
            UnrecognisedAllergies = a.UnrecognisedAllergies.Concat(b.UnrecognisedAllergies).ToList()
        };
    }
}
