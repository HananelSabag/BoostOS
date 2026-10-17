namespace BoostOS.Core;

public enum CheckStatus { Ok, Warn, Bad, Info }

/// <summary>A problem BoostOS knows how to fix by itself. NeedsAdmin fixes run in one elevated helper (one UAC prompt).</summary>
public sealed record FixRef(string Id, bool NeedsAdmin);

/// <summary>One line on the diagnostics screen: what we looked at, what we found, what to do, and whether we can do it.</summary>
public sealed record Check(string Title, string Value, CheckStatus Status, string? Advice = null, FixRef? Fix = null);

public sealed record Section(string Id, string Title, string Icon, IReadOnlyList<Check> Checks);

public sealed record ScanReport(DateTime GeneratedAt, int Score, string Machine, IReadOnlyList<Section> Sections);
