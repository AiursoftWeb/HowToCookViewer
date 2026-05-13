using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.ManageViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Manage";
    }

    [Display(Name = "Allow user to adjust nickname")]
    public bool AllowUserAdjustNickname { get; set; }
}
