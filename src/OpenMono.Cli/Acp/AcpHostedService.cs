using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenMono.Acp;

/// <summary>
/// Owns the lifetime of the ACP <see cref="WebApplication"/> and the discovery
/// lock file. Implements <see cref="IHostedService"/> for forward compatibility
/// with the .NET Generic Host (Program.cs currently calls
/// <see cref="StartAsync"/>/<see cref="StopAsync"/> directly so we don't pay
/// for a full Host today).
///
/// Lifecycle:
/// <list type="number">
///   <item><see cref="StartAsync"/> builds Kestrel + Map (via <see cref="AcpServer.Build"/>),
///         starts listening, and only then writes the lock file. Writing it earlier would
///         publish a port the extension could try to connect to before Kestrel was ready.</item>
///   <item><see cref="StopAsync"/> removes the lock file first (so a racing extension
///         doesn't latch onto a soon-dead port), then drains Kestrel.</item>
/// </list>
/// </summary>
public sealed class AcpHostedService : IHostedService, IAsyncDisposable
{
    private readonly AcpServerSettings _settings;
    private readonly IServiceCollection _services;
    private readonly AcpLockFileWriter? _lockfile;
    private WebApplication? _app;

    public AcpHostedService(
        AcpServerSettings settings,
        IServiceCollection services,
        AcpLockFileWriter? lockfile = null)
    {
        _settings = settings;
        _services = services;
        _lockfile = lockfile;
    }

    /// <summary>The bound WebApplication, available after <see cref="StartAsync"/>.</summary>
    public WebApplication App => _app
        ?? throw new InvalidOperationException("AcpHostedService has not been started.");

    public async Task StartAsync(CancellationToken ct)
    {
        if (_app is not null) return;
        _app = AcpServer.Build(_settings, _services);
        await _app.StartAsync(ct);
        _lockfile?.Write();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _lockfile?.TryRemove();
        if (_app is not null)
        {
            await _app.StopAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
