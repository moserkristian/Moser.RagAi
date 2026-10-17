using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Moser.RagAi.Web;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresHealthCheck> _logger;

    public PostgresHealthCheck(NpgsqlDataSource dataSource, ILogger<PostgresHealthCheck> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = _dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres health check failed.");
            return HealthCheckResult.Unhealthy("Postgres is not reachable.");
        }
    }
}

public sealed class EmbeddingFilesHealthCheck : IHealthCheck
{
    private readonly EmbeddingLocation _location;

    public EmbeddingFilesHealthCheck(EmbeddingLocation location)
    {
        _location = location;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var model = Path.Combine(_location.Path, EmbeddingFiles.ModelFileName);
        var vocab = Path.Combine(_location.Path, EmbeddingFiles.VocabFileName);
        if (File.Exists(model) && File.Exists(vocab))
            return Task.FromResult(HealthCheckResult.Healthy());

        return Task.FromResult(HealthCheckResult.Unhealthy("The local embedding model is not on disk."));
    }
}
