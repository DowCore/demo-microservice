using Volo.Abp.Reflection;

namespace Meta.Dow.Administration.Permissions;

public static class AdministrationPermissions
{
    public const string GroupName = "Administration";

    public static class Settings
    {
        public const string Default = GroupName + ".Settings";
        public const string Update = Default + ".Update";
    }

    public static class Menus
    {
        public const string Default = GroupName + ".Menus";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class DataScopes
    {
        public const string Default = GroupName + ".DataScopes";
        public const string Manage = Default + ".Manage";
    }

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(AdministrationPermissions));
    }
}
