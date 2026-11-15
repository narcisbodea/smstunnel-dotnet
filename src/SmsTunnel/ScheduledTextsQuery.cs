namespace SmsTunnel;

/// <summary>Which scheduled texts to list.</summary>
public enum ScheduledTextScope
{
    /// <summary>The ones waiting for their time, soonest first.</summary>
    Waiting = 0,

    /// <summary>The ones that went out, failed or were canceled, newest first. SMSTunnel keeps them for 30 days.</summary>
    Done,
}

/// <summary>Which of the texts this API key scheduled to list. Every setting is optional.</summary>
public sealed class ScheduledTextsQuery
{
    /// <summary>The waiting ones (the default) or the done ones.</summary>
    public ScheduledTextScope Scope { get; set; }

    /// <summary>The page, starting at 1.</summary>
    public int? Page { get; set; }

    /// <summary>How many texts per page, 1 to 100. SMSTunnel's default is 50.</summary>
    public int? Limit { get; set; }
}
