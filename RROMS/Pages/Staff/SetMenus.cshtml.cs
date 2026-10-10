using RROMS.Services;

namespace RROMS.Pages.Staff;

/// The set menus guests can choose for the table. Staff and administrators both create them (see <see cref="SetMenusListPageBase"/>).
public class SetMenusModel : SetMenusListPageBase
{
    public SetMenusModel(ISetMenuService sets) : base(sets) { }
    public override string Area => "Staff";
}
