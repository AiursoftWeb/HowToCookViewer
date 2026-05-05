using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.PermissionsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Permissions";
    }

    public required List<PermissionWithRoleCount> Permissions { get; init; }
}
