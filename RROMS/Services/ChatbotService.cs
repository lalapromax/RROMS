using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

public interface IChatbotService
{
    Task<string> GetAnswerAsync(string userMessage, string? userId = null);
}

public class ChatbotService : IChatbotService
{
    private readonly ApplicationDbContext _context;

    public ChatbotService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> GetAnswerAsync(string userMessage, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return "Ask me about opening hours, making a reservation, our menu, or your recent order.";

        var text = userMessage.ToLowerInvariant();

        // Intent: opening hours today
        if (Contains(text, "hour", "open", "close", "closing", "opening"))
        {
            var today = await _context.OperatingHours.FirstOrDefaultAsync(h => h.DayOfWeek == DateTime.Today.DayOfWeek);
            if (today != null)
            {
                return today.IsClosed
                    ? "We're closed today. Please check our Home page for the full weekly schedule."
                    : $"Today ({DateTime.Today:dddd}) we're open from {FormatTime(today.OpenTime)} to {FormatTime(today.CloseTime)}.";
            }
        }

        // Intent: "my reservation" / "my order" status, when we know who is asking
        if (!string.IsNullOrEmpty(userId) && Contains(text, "my reservation", "my booking"))
        {
            var next = await _context.Reservations
                .Where(r => r.CustomerId == userId && r.ReservationDateTime >= DateTime.Now
                            && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Pending))
                .OrderBy(r => r.ReservationDateTime)
                .FirstOrDefaultAsync();

            return next == null
                ? "I can't find any upcoming reservations on your account. Would you like to make one?"
                : $"Your next reservation is on {next.ReservationDateTime:dddd, dd MMM} at {next.ReservationDateTime:HH:mm} for {next.PartySize} guest(s) - status: {next.Status}.";
        }

        if (!string.IsNullOrEmpty(userId) && Contains(text, "my order"))
        {
            var latest = await _context.Orders
                .Where(o => o.CustomerId == userId)
                .OrderByDescending(o => o.OrderDate)
                .FirstOrDefaultAsync();

            return latest == null
                ? "You don't have any orders yet. Head to the Menu page to place one!"
                : $"Your most recent order (#{latest.Id}) is currently: {latest.Status}, total {latest.TotalAmount:C}.";
        }

        // Fallback: keyword scoring against the FAQ knowledge base
        var faqs = await _context.ChatbotFaqs.Where(f => f.IsActive).ToListAsync();
        var messageWords = text.Split(new[] { ' ', ',', '?', '.', '!' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        ChatbotFaq? best = null;
        var bestScore = 0;
        foreach (var faq in faqs)
        {
            var keywords = faq.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var score = keywords.Count(k => text.Contains(k.ToLowerInvariant()) || messageWords.Contains(k.ToLowerInvariant()));
            if (score > bestScore)
            {
                bestScore = score;
                best = faq;
            }
        }

        if (best != null && bestScore > 0)
            return best.Answer;

        return "I'm not sure about that one yet. Try asking about opening hours, reservations, the menu, orders, or payments - or reach out to our staff directly.";
    }

    private static bool Contains(string text, params string[] needles) => needles.Any(text.Contains);

    private static string FormatTime(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");
}
