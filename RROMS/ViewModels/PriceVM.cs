namespace RROMS.ViewModels;

/// A price on a card. DiscountPercent is the running promotion for this guest (0 when there is none).
public record PriceVM(decimal Price, decimal DiscountPercent = 0, string? Suffix = null)
{
    public bool HasDiscount => DiscountPercent > 0;
    public decimal Now => Math.Round(Price * (1 - DiscountPercent / 100m), 2);
}
