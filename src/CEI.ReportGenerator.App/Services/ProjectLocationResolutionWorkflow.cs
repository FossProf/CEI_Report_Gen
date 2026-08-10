using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.Services;

public sealed class ProjectLocationResolutionWorkflow(IProjectLocationResolver locationResolver)
{
    public async Task<ProjectLocationResolutionOutcome> ResolveAsync(
        Project? currentProject,
        string locationText,
        CancellationToken cancellationToken)
    {
        var trimmedLocationText = locationText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLocationText))
        {
            return ProjectLocationResolutionOutcome.Cleared();
        }

        if (currentProject is not null
            && string.Equals(currentProject.LocationText, trimmedLocationText, StringComparison.Ordinal)
            && currentProject.Coordinates is not null)
        {
            return ProjectLocationResolutionOutcome.FromCached(trimmedLocationText, currentProject.Coordinates);
        }

        var resolved = await locationResolver.ResolveAsync(trimmedLocationText, cancellationToken);
        return resolved is null
            ? ProjectLocationResolutionOutcome.Unresolved(trimmedLocationText)
            : ProjectLocationResolutionOutcome.Resolved(trimmedLocationText, resolved);
    }
}

public sealed record ProjectLocationResolutionOutcome(
    string LocationText,
    ProjectCoordinates? Coordinates,
    bool UsedCachedCoordinates,
    bool IsResolved)
{
    public static ProjectLocationResolutionOutcome Cleared()
        => new(string.Empty, null, UsedCachedCoordinates: false, IsResolved: false);

    public static ProjectLocationResolutionOutcome FromCached(string locationText, ProjectCoordinates coordinates)
        => new(locationText, coordinates, UsedCachedCoordinates: true, IsResolved: true);

    public static ProjectLocationResolutionOutcome Resolved(string locationText, ProjectCoordinates coordinates)
        => new(locationText, coordinates, UsedCachedCoordinates: false, IsResolved: true);

    public static ProjectLocationResolutionOutcome Unresolved(string locationText)
        => new(locationText, null, UsedCachedCoordinates: false, IsResolved: false);
}
