namespace BrightPath.Api.Data;

/// <summary>
/// Imports the provided historical CSV exports. The importer is deliberately
/// deferred so its grouping and history-preservation rules remain explicit.
/// </summary>
public sealed class SeedDataService
{
    public Task SeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
