namespace AgentHub.Api;

public enum AcceptMode
{
    AutoAccept,
    AskOwnerFirst,
    NeverAuto
}

public enum ContactMode
{
    Poll,
    Webhook,
    ManualOnly
}

public enum TaskStatus
{
    Pending,
    Claimed,
    Completed,
    Failed
}
