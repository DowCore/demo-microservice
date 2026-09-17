using Volo.Abp.Reflection;

namespace Meta.Dow.SaaS.Permissions;

public static class OrchestrationPermissions
{
    public const string GroupName = "Orchestration";

    public static class Definitions
    {
        public const string Default = GroupName + ".Definitions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Publish = Default + ".Publish";
    }

    public static class Instances
    {
        public const string Default = GroupName + ".Instances";
        public const string Run = Default + ".Run";
        public const string Cancel = Default + ".Cancel";
    }

    public static class DataSources
    {
        public const string Default = GroupName + ".DataSources";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Sql
    {
        /// <summary>Code/Sql 节点写库（execute/batch）</summary>
        public const string Write = GroupName + ".Sql.Write";
    }

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(OrchestrationPermissions));
    }
}
