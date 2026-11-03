using System.Text.Json;

namespace SmsTunnel;

/// <summary>
/// Your plan, today's usage and what is left, from <see cref="ISmsTunnelClient.GetUsageAsync"/>. Daily counts restart
/// at 00:00 UTC, the month's count on the 1st. A client of a company account shares the company's allowance and sees it here.
/// </summary>
public sealed record Usage
{
    /// <summary>The plan, for example <c>free</c>, <c>starter</c>, <c>standard</c>, <c>pro</c> or <c>enterprise</c>.</summary>
    public string? Plan { get; init; }

    /// <summary>The kind of account, for example <c>standard</c>.</summary>
    public string? AccountType { get; init; }

    /// <summary>What was used today (and this month).</summary>
    public required UsageCounts Used { get; init; }

    /// <summary>The plan's limits. <see langword="null"/> means no limit.</summary>
    public required UsageLimits Limits { get; init; }

    /// <summary>What is left. <see langword="null"/> means no limit.</summary>
    public required UsageRemaining Remaining { get; init; }

    /// <summary>The company account whose allowance this account shares, if any.</summary>
    public UsageEnterprise? Enterprise { get; init; }

    /// <summary>SMSTunnel's whole answer, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>What was used. The names are the API's (<c>usage.smsClean</c> and so on).</summary>
public sealed record UsageCounts
{
    /// <summary>Texts sent today as written.</summary>
    public long SmsClean { get; init; }

    /// <summary>Texts sent today with the added SMSTunnel line (Free plan).</summary>
    public long SmsBranded { get; init; }

    /// <summary>Sign-in codes sent today.</summary>
    public long TwoFaClean { get; init; }

    /// <summary>Texts sent this month as written.</summary>
    public long SmsCleanMonth { get; init; }
}

/// <summary>The plan's limits (<c>limits.*</c>). <see langword="null"/> means no limit.</summary>
public sealed record UsageLimits
{
    /// <summary>Texts as written, per day.</summary>
    public long? SmsClean { get; init; }

    /// <summary>Sign-in codes, per day.</summary>
    public long? TwoFaClean { get; init; }

    /// <summary>Texts as written, per month.</summary>
    public long? SmsCleanPerMonth { get; init; }

    /// <summary>Texts with the added SMSTunnel line, per day (Free plan; 0 on paid plans).</summary>
    public long? SmsBrandedPerDay { get; init; }
}

/// <summary>What is left (<c>remaining.*</c>). <see langword="null"/> means no limit.</summary>
public sealed record UsageRemaining
{
    /// <summary>Texts that can still go out today as written (this month's count included).</summary>
    public long? SmsClean { get; init; }

    /// <summary>Sign-in codes that can still go out today.</summary>
    public long? TwoFaClean { get; init; }

    /// <summary>Texts as written left this month.</summary>
    public long? SmsCleanMonth { get; init; }

    /// <summary>Texts with the added SMSTunnel line left today.</summary>
    public long? SmsBranded { get; init; }
}

/// <summary>The company account a client account belongs to.</summary>
public sealed record UsageEnterprise
{
    /// <summary>The company account's ID.</summary>
    public string? Id { get; init; }

    /// <summary>The company's app name.</summary>
    public string? Name { get; init; }
}
