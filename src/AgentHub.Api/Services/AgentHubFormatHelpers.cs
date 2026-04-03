namespace AgentHub.Api;

public static class AgentHubFormatHelpers
{
    public static string[] NormalizeStrings(IEnumerable<string>? values) =>
        values?.Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    public static string JoinList(IEnumerable<string>? values) => string.Join('|', NormalizeStrings(values));

    public static string[] SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string JoinGuidList(IEnumerable<Guid>? values) => string.Join('|', values?.Distinct() ?? []);

    public static Guid[] SplitGuidList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Guid.Parse).ToArray();
}
