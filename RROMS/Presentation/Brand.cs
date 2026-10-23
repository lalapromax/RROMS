namespace RROMS.Presentation;

/// 
/// The restaurant's identity. The name is fixed in code on purpose: nobody can rename the restaurant from inside the
/// site, not even an administrator. It is shown on every page, in emails and on receipts from this one place.
/// 
public static class Brand
{
    public const string Name = "Brasserie La Mélancolie";

    /// The logo goes here. Drop the file in and every page picks it up; until then the bars show an empty outlined slot.
    public const string LogoPath = "/static/assets/logo.png";

    public const string Currency = "RM";
}
