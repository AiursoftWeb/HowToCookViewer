namespace Aiursoft.HowToCookViewer.Authorization;

/// <summary>
/// A static class that provides all application permissions.
/// It uses a fake localizer to ensure permission names and descriptions are picked up by localization tools.
/// This class serves as the single source of truth for all permissions in the application.
/// </summary>
public class AppPermissions
{
    public const string Type = "Permission";

    /// <summary>
    /// A fake localizer that returns the input string as is.
    /// This is used to trick auto scanning tools to detect these strings for localization.
    /// </summary>
    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    public static List<PermissionDescriptor> GetAllPermissions()
    {
        // Make a fake localizer. This returns as is.
        // This trick is to make auto scanning tools to detect these strings for localization.
        var localizer = new FakeLocalizer();
        List<PermissionDescriptor> allPermission =
        [
            new(AppPermissionNames.CanReadUsers,
                localizer["Read Users"],
                localizer["Allows viewing the list of all users."]),
            new(AppPermissionNames.CanDeleteUsers,
                localizer["Delete Users"],
                localizer["Allows the permanent deletion of user accounts."]),
            new(AppPermissionNames.CanAddUsers,
                localizer["Add New Users"],
                    localizer["Grants permission to create new user accounts."]),
            new(AppPermissionNames.CanEditUsers,
                localizer["Edit User Information"],
                    localizer["Allows modification of user details like email and roles, and can also reset user passwords."]),
            new(AppPermissionNames.CanReadRoles,
                localizer["Read Roles"],
                    localizer["Allows viewing the list of roles and their assigned permissions."]),
            new(AppPermissionNames.CanDeleteRoles,
                localizer["Delete Roles"],
                localizer["Allows the permanent deletion of roles."]),
            new(AppPermissionNames.CanAddRoles,
                localizer["Add New Roles"],
                localizer["Grants permission to create new roles."]),
            new(AppPermissionNames.CanEditRoles,
                localizer["Edit Role Information"],
                localizer["Allows modification of role names and their assigned permissions."]),
            new(AppPermissionNames.CanAssignRoleToUser,
                localizer["Assign Roles to Users"],
                localizer["Allows assigning or removing roles for any user."]),
            new(AppPermissionNames.CanReadPermissions,
                localizer["Read Permissions"],
                localizer["Allows viewing the list of all permissions and their assignments to roles and users."]),
            new(AppPermissionNames.CanViewSystemContext,
                localizer["View System Context"],
                localizer["Allows viewing system-level information and settings."]),
            new(AppPermissionNames.CanRebootThisApp,
                localizer["Reboot This App"],
                localizer["Grants permission to restart the application instance. May cause availability interruptions but all settings and cache will be reloaded."]),
            new(AppPermissionNames.CanViewBackgroundJobs,
                localizer["View Background Jobs"],
                localizer["Allows viewing the background job dashboard and managing jobs."]),
            new(AppPermissionNames.CanManageGlobalSettings,
                localizer["Manage Global Settings"],
                localizer["Allows viewing and modifying global application settings."])
        ];
        return allPermission;
    }
}
