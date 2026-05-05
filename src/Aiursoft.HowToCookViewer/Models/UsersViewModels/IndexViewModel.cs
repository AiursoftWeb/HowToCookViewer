using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.UsersViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Users";
    }

    public required List<UserWithRolesViewModel> Users { get; set; }
}
