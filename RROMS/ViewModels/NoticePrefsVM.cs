namespace RROMS.ViewModels;

/// The four "how we reach you" switches. Booking, order and payment updates are not here: they always arrive.
public record NoticePrefsVM(bool Announcements, bool Rewards, bool Offers, bool ByEmail);
