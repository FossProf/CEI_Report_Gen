namespace CEI.ReportGenerator.App;

public sealed class ProjectReadiness
{
    public bool TemplateReady { get; init; }

    public bool InspectorSignatureReady { get; init; }

    public bool ProjectManagerSignatureReady { get; init; }

    public bool ProjectConfigurationReady { get; init; }

    public IReadOnlyList<string> TemplateIssues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> InspectorSignatureIssues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ProjectManagerSignatureIssues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ProjectConfigurationIssues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public bool IsReady =>
        TemplateReady &&
        InspectorSignatureReady &&
        ProjectManagerSignatureReady &&
        ProjectConfigurationReady;
}
