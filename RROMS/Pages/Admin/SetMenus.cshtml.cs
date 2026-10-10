using RROMS.Services;

namespace RROMS.Pages.Admin;

/// The set menus guests can choose for the table. Administrators and staff use the same screens (see <see cref="SetMenusListPageBase"/>).
public class SetMenusModel : SetMenusListPageBase
{
    public SetMenusModel(ISetMenuService sets) : base(sets) { }
    public override string Area => "Admin";
}
