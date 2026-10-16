using RROMS.Models;

namespace RROMS.ViewModels;

/// One line in the Notices list. <paramref name="Id"/> is the anchor other pages link to (#a12 for an announcement).
public record NoticeRow(string Id, string Kind, string Icon, string Title, string Body, DateTime When, bool IsNew, int? NotificationId, string? Link);

/// The rows under the tabs. <paramref name="HasPinned"/> only stops the "all quiet" message when the pinned notice is already on the page.
public record NoticeListVM(IReadOnlyList<NoticeRow> Rows, bool HasPinned, string EmptyText);

/// The tab links above the list: key, label, how many are in each, and which is open.
public record NoticeTabsVM(IReadOnlyList<(string Key, string Label)> Tabs, string Current, IReadOnlyDictionary<string, int> Counts);
