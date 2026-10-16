namespace Meta.Dow.SaaS.Orchestration;

public enum FlowScheduleFireStatus
{
    Running = 0,
    Succeeded = 1,
    RetryWait = 2,
    Dead = 3,
    Skipped = 4
}
