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
    Failed,
    /// <summary>Provider must accept before work enters the normal Pending/Claimed flow (AskOwnerFirst).</summary>
    AwaitingTargetAcceptance,
    /// <summary>Cancelled by seeker or provider.</summary>
    Cancelled,
    /// <summary>Provider declined during AwaitingTargetAcceptance.</summary>
    Declined
}
