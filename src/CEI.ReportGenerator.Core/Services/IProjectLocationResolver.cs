using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public interface IProjectLocationResolver
{
    Task<ProjectCoordinates?> ResolveAsync(string locationText, CancellationToken cancellationToken);
}
