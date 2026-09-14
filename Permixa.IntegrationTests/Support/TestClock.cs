using Permixa.Application.Common.Abstractions;

namespace Permixa.IntegrationTests.Support;

public sealed class TestClock : IClock
{
    private DateTime _utcNow = DateTime.UtcNow;

    public DateTime UtcNow => _utcNow;

    public void Advance(TimeSpan delta) =>
        _utcNow = DateTime.SpecifyKind(_utcNow.Add(delta), DateTimeKind.Utc);
}
