using Microsoft.Extensions.Time.Testing;
using PcSante.Core.Actions;
using PcSante.Core.Windows;

namespace PcSante.Core.Tests;

public class RestorePointGuardTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Point_recent_reutilise()
    {
        var api = new FakeRestorePointApi();
        api.Points.Add(new RestorePointInfo(1, "x", _time.GetUtcNow().AddMinutes(-10)));

        (await new RestorePointGuard(api, _time).EnsureAsync("d", default)).Should().BeTrue();
        api.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Point_ancien_nouveau_point_cree()
    {
        var api = new FakeRestorePointApi { Clock = _time.GetUtcNow };
        api.Points.Add(new RestorePointInfo(1, "x", _time.GetUtcNow().AddDays(-2)));

        (await new RestorePointGuard(api, _time).EnsureAsync("d", default)).Should().BeTrue();
        api.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Creation_refusee_par_windows()
    {
        var api = new FakeRestorePointApi { CreateSucceeds = false };

        (await new RestorePointGuard(api, _time).EnsureAsync("d", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Restauration_desactivee()
    {
        (await new RestorePointGuard(new FakeRestorePointApi { Enabled = false }, _time).EnsureAsync("d", default)).Should().BeFalse();
    }
}
