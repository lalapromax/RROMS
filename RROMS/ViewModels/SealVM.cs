namespace RROMS.ViewModels;

/// The restaurant seal in a bar or the footer: where it links to and how a screen reader should describe it.
public record SealVM(string Href, string Aria, bool HasLogo);
