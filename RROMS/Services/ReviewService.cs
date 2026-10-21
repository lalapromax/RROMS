using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// One thing a customer can review: a completed order, or a completed dining visit that had no order.
public record ReviewTarget(string Key, string Label);

/// Everything the "Leave a review" form posts. Validated again in <see cref="ReviewService"/> - never trusted.
public class ReviewSubmission
{
    /// "order:12" or "reservation:7".
    public string? Target { get; set; }
    public int? Food { get; set; }
    public int? Service { get; set; }
    public int? Cleanliness { get; set; }
    public int? Wait { get; set; }
    public int? Value { get; set; }
    public int? RecommendScore { get; set; }
    public IEnumerable<string>? IssueTags { get; set; }
    public string? Comments { get; set; }
    public bool ShowFirstName { get; set; } = true;
    public IReadOnlyList<IFormFile> Images { get; set; } = Array.Empty<IFormFile>();

    /// Editing only: the photos already on the review that the guest wants to keep.
    public IEnumerable<string>? KeepImages { get; set; }
}

public interface IReviewService
{
    /// Orders / visits this customer may still review (completed, theirs, not yet reviewed).
    Task<List<ReviewTarget>> GetEligibleTargetsAsync(string customerId);

    Task<(bool Success, string Message)> SubmitAsync(string customerId, ReviewSubmission submission);

    /// Changes the guest's own review, within <see cref="Models.Feedback.GuestEditDays"/> days of writing it.
    Task<(bool Success, string Message)> UpdateAsync(string customerId, int feedbackId, ReviewSubmission submission);

    /// Takes back the guest's own review (and its photos). The order or visit can then be reviewed again.
    Task<(bool Success, string Message)> DeleteAsync(string customerId, int feedbackId);
}

public class ReviewService : IReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly IReviewImageService _images;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(ApplicationDbContext context, IReviewImageService images, ILogger<ReviewService> logger)
    {
        _context = context;
        _images = images;
        _logger = logger;
    }

    public async Task<List<ReviewTarget>> GetEligibleTargetsAsync(string customerId)
    {
        var orders = await _context.Orders
            .Where(o => o.CustomerId == customerId
                        && o.Status == OrderStatus.Completed
                        && !_context.Feedbacks.Any(f => f.OrderId == o.Id))
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        // A visit that had a completed order is reviewed through that order, so the same meal
        // is never reviewed twice. A reservation with no completed order is reviewable by itself.
        var reservations = await _context.Reservations
            .Where(r => r.CustomerId == customerId
                        && r.Status == ReservationStatus.Completed
                        && !r.Orders.Any(o => o.Status == OrderStatus.Completed)
                        && !_context.Feedbacks.Any(f => f.ReservationId == r.Id))
            .OrderByDescending(r => r.ReservationDateTime)
            .ToListAsync();

        var targets = new List<ReviewTarget>();
        targets.AddRange(orders.Select(o => new ReviewTarget(
            "order:" + o.Id,
            $"Order #{o.Id}, {o.OrderDate.ToLocalTime():d MMM yyyy}")));
        targets.AddRange(reservations.Select(r => new ReviewTarget(
            "reservation:" + r.Id,
            $"Dining visit, {r.ReservationDateTime:d MMM yyyy}, table for {r.PartySize}")));
        return targets;
    }

    /// Checks everything the guest typed, and works out the overall stars from the parts they rated.
    private static (string? Error, int Overall, string? Comments, List<string> Tags) Validate(ReviewSubmission s)
    {
        var scores = new[] { s.Food, s.Service, s.Cleanliness, s.Wait, s.Value };
        foreach (var score in scores)
        {
            if (score.HasValue && (score < 1 || score > 5))
                return ("Ratings must be between 1 and 5 stars.", 0, null, new());
        }

        // The overall star count is not asked for: it is the average of the parts the guest rated, rounded to the nearest star.
        var answered = scores.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (answered.Count == 0)
            return ("Please rate at least one part of your visit with the stars.", 0, null, new());
        var overall = (int)Math.Round(answered.Average(), MidpointRounding.AwayFromZero);

        if (s.RecommendScore.HasValue && (s.RecommendScore < 0 || s.RecommendScore > 10))
            return ("The recommendation score must be between 0 and 10.", 0, null, new());

        var comments = string.IsNullOrWhiteSpace(s.Comments) ? null : s.Comments.Trim();
        if (comments != null && comments.Length > ReviewOptions.MaxCommentLength)
            return ($"Please keep your comments under {ReviewOptions.MaxCommentLength} characters.", 0, null, new());

        // Only tags from the fixed list are kept; anything else a tampered form sends is dropped.
        var tags = (s.IssueTags ?? Enumerable.Empty<string>())
            .Select(t => ReviewOptions.IssueTags.FirstOrDefault(allowed => allowed.Equals(t?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(t => t != null)
            .Select(t => t!)
            .Distinct()
            .ToList();

        return (null, overall, comments, tags);
    }

    public async Task<(bool Success, string Message)> SubmitAsync(string customerId, ReviewSubmission s)
    {
        // ---- 1. Scores: re-checked here whatever the form or its client-side script allowed ----
        var (error, overall, comments, tags) = Validate(s);
        if (error != null) return (false, error);

        if (s.Images.Count > ReviewOptions.MaxImages)
            return (false, $"You can add up to {ReviewOptions.MaxImages} photos.");

        // ---- 2. The target must be this customer's own, completed, and not yet reviewed ----
        int? orderId = null;
        int? reservationId = null;
        var parts = (s.Target ?? string.Empty).Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var targetId))
            return (false, "Please choose what you are reviewing.");

        if (parts[0] == "order")
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == targetId && o.CustomerId == customerId);
            if (order == null || order.Status != OrderStatus.Completed)
                return (false, "That order can't be reviewed. Only your own completed orders can.");
            if (await _context.Feedbacks.AnyAsync(f => f.OrderId == order.Id))
                return (false, "You've already reviewed that order.");
            orderId = order.Id;
        }
        else if (parts[0] == "reservation")
        {
            var reservation = await _context.Reservations
                .Include(r => r.Orders)
                .FirstOrDefaultAsync(r => r.Id == targetId && r.CustomerId == customerId);
            if (reservation == null || reservation.Status != ReservationStatus.Completed)
                return (false, "That visit can't be reviewed. Only your own completed visits can.");
            if (reservation.Orders.Any(o => o.Status == OrderStatus.Completed))
                return (false, "That visit had an order, so please review the order instead.");
            if (await _context.Feedbacks.AnyAsync(f => f.ReservationId == reservation.Id))
                return (false, "You've already reviewed that visit.");
            reservationId = reservation.Id;
        }
        else
        {
            return (false, "Please choose what you are reviewing.");
        }

        // ---- 3. Photos: validate and store; if any one is rejected, keep none of them ----
        var savedImages = new List<string>();
        foreach (var file in s.Images.Where(f => f != null && f.Length > 0))
        {
            var (ok, message, url) = await _images.SaveAsync(file, customerId);
            if (!ok || url == null)
            {
                foreach (var saved in savedImages) _images.DeleteIfOurs(saved);
                return (false, message);
            }
            savedImages.Add(url);
        }

        // ---- 4. Save. The unique indexes are the final guard against a double-submit race ----
        var feedback = new Models.Feedback
        {
            CustomerId = customerId,
            OrderId = orderId,
            ReservationId = reservationId,
            Rating = overall,
            FoodRating = s.Food,
            ServiceRating = s.Service,
            CleanlinessRating = s.Cleanliness,
            WaitTimeRating = s.Wait,
            ValueRating = s.Value,
            RecommendScore = s.RecommendScore,
            IssueTags = tags.Count > 0 ? string.Join(",", tags) : null,
            Comments = comments,
            ImagePaths = savedImages.Count > 0 ? string.Join(",", savedImages) : null,
            ShowFirstName = s.ShowFirstName,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _context.Feedbacks.Add(feedback);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            foreach (var saved in savedImages) _images.DeleteIfOurs(saved);
            _logger.LogWarning(ex, "Review for customer {CustomerId} was rejected by the database (likely a duplicate).", customerId);
            return (false, "We couldn't save that review - it may already have been submitted.");
        }

        return (true, "Thank you! Your review has been submitted.");
    }

    public async Task<(bool Success, string Message)> UpdateAsync(string customerId, int feedbackId, ReviewSubmission s)
    {
        var review = await _context.Feedbacks.FirstOrDefaultAsync(f => f.Id == feedbackId && f.CustomerId == customerId);
        if (review == null)
            return (false, "We could not find that review.");
        if (!review.GuestCanEdit)
            return (false, $"A review can be changed for {Models.Feedback.GuestEditDays} days after it is written. This one is now closed.");

        var (error, overall, comments, tags) = Validate(s);
        if (error != null) return (false, error);

        // Photos: keep only the ones that really belong to this review, then add new ones up to the limit.
        var current = review.ImageList().ToList();
        var keep = (s.KeepImages ?? Enumerable.Empty<string>())
            .Select(k => k?.Trim())
            .Where(k => !string.IsNullOrEmpty(k) && current.Contains(k!))
            .Select(k => k!)
            .Distinct()
            .ToList();
        var fresh = s.Images.Where(f => f != null && f.Length > 0).ToList();
        if (keep.Count + fresh.Count > ReviewOptions.MaxImages)
            return (false, $"You can have up to {ReviewOptions.MaxImages} photos on a review.");

        var added = new List<string>();
        foreach (var file in fresh)
        {
            var (ok, message, url) = await _images.SaveAsync(file, customerId);
            if (!ok || url == null)
            {
                foreach (var saved in added) _images.DeleteIfOurs(saved);
                return (false, message);
            }
            added.Add(url);
        }

        // Tags from an earlier version of the list are not on the form, so they are carried over rather than silently lost.
        var carried = review.IssueTagList().Where(t => !ReviewOptions.IssueTags.Any(a => a.Equals(t, StringComparison.OrdinalIgnoreCase)));
        var allTags = tags.Concat(carried).Distinct().ToList();

        review.Rating = overall;
        review.FoodRating = s.Food;
        review.ServiceRating = s.Service;
        review.CleanlinessRating = s.Cleanliness;
        review.WaitTimeRating = s.Wait;
        review.ValueRating = s.Value;
        review.RecommendScore = s.RecommendScore;
        review.IssueTags = allTags.Count > 0 ? string.Join(",", allTags) : null;
        review.Comments = comments;
        review.ShowFirstName = s.ShowFirstName;
        var all = keep.Concat(added).ToList();
        review.ImagePaths = all.Count > 0 ? string.Join(",", all) : null;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            foreach (var saved in added) _images.DeleteIfOurs(saved);
            _logger.LogWarning(ex, "Edit of review {ReviewId} for customer {CustomerId} could not be saved.", feedbackId, customerId);
            return (false, "We could not save your changes. Please try again.");
        }

        foreach (var gone in current.Where(c => !keep.Contains(c))) _images.DeleteIfOurs(gone);
        return (true, "Your review has been updated.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(string customerId, int feedbackId)
    {
        var review = await _context.Feedbacks.FirstOrDefaultAsync(f => f.Id == feedbackId && f.CustomerId == customerId);
        if (review == null)
            return (false, "We could not find that review.");

        var photos = review.ImageList().ToList();
        _context.Feedbacks.Remove(review);
        await _context.SaveChangesAsync();
        foreach (var p in photos) _images.DeleteIfOurs(p);
        return (true, "Your review has been taken down.");
    }
}
