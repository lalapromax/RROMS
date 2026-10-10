using RROMS.Data;
using RROMS.Services;

namespace RROMS.Pages.Admin.SetMenus;

public class EditModel : SetMenuEditPageBase
{
    public EditModel(ISetMenuService sets, ApplicationDbContext context, IAdminImageService images) : base(sets, context, images) { }
    public override string Area => "Admin";
}
