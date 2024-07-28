using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PackageDeployer.Core;

public enum MainMenuOptions
{
    [Description("Exit")] [Display(Name = "Exit")]
    Exit = 0,

    [Description("Publish existing repository")] [Display(Name = "Publish existing repository")]
    PublishExistingRepository = 1,

    [Description("Manage existing repository")] [Display(Name = "Manage existing repository")]
    ManageExistingRepository = 2,

    [Description("Add new repository")] [Display(Name = "Add new repository")]
    AddNewRepository = 3
}