namespace Meta.Dow.SaaS.Orchestration;

public static class OrchestrationConsts
{
    public const int MaxNameLength = 128;
    public const int MaxCodeLength = 64;
    public const int MaxCategoryLength = 64;
    public const int MaxConnectionStringLength = 2048;
    public const int MaxDataSourceCodeLength = 64;
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
}
