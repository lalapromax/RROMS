namespace RROMS.ViewModels;

/// One button under the message. <paramref name="SignOutFirst"/> turns it into a small form that signs the visitor out and then goes to <paramref name="Href"/>.
public record ErrorAct(string Text, string Href, bool Primary = false, bool SignOutFirst = false);

/// One of the three "when something is missing" cards: not found, not for this account, our side.
/// <param name="Kind">missing, denied or ours. Picks the plate colour.</param>
/// <param name="Code">The small italic line above the heading (404, Not for this account, Our side).</param>
/// <param name="Reference">Only for "our side": the short code the guest can read out on the phone.</param>
public record ErrorCardVM(string Kind, string Code, string Title, string Text, IReadOnlyList<ErrorAct> Acts, string? Reference = null);
