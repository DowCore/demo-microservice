namespace Meta.Dow.SaaS.Orchestration;

public static class OrchestrationConsts
{
    public const int MaxNameLength = 128;
    public const int MaxCodeLength = 64;
    public const int MaxCategoryLength = 64;
    public const int MaxConnectionStringLength = 2048;
    public const int MaxDataSourceCodeLength = 64;
    public const int MaxTableNameLength = 64;
    public const int MaxColumnNameLength = 64;
    public const int MaxResourceCodeLength = 64;
    public const int ResourceQueryMaxPageSize = 200;
    public const int MaxFilterGroupDepth = 3;
    public const int MaxMessageSourceCodeLength = 64;
    public const int MaxTriggerCodeLength = 64;
    public const int MaxScheduleCodeLength = 64;
    public const int MaxCronLength = 128;
    public const int MaxTimeZoneLength = 64;
    public const int CodeScriptTimeoutSeconds = 8;
    public const int CodeMaxStatements = 20_000;
    public const int CodeDbBatchMaxRows = 500;
    public const int CodeDbQueryMaxRows = 1_000;
    public const int CodeDbCommandTimeoutSeconds = 15;
    public const string DslVersion = "1.1";

    /// <summary>SubFlow 最大嵌套深度（含当前流程）</summary>
    public const int MaxSubFlowDepth = 5;

    /// <summary>ForEach 默认最大迭代次数（未显式配置时生效）</summary>
    public const int ForEachDefaultMaxIterations = 1000;

    /// <summary>ForEach 硬上限：即便 DSL 要求更多也截断在此，防止失控</summary>
    public const int ForEachHardMaxIterations = 10_000;

    /// <summary>ForEach 并发度上限</summary>
    public const int ForEachMaxConcurrency = 16;

    /// <summary>Parallel 最大分支数（出边数）</summary>
    public const int ParallelMaxBranches = 16;

    /// <summary>Parallel / ForEach 嵌套深度上限（与 SubFlow 共用同一调用栈计数）</summary>
    public const int MaxControlNestingDepth = 5;
}
