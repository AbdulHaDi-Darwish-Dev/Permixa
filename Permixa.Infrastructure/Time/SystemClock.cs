using Permixa.Application.Common.Abstractions;

namespace Permixa.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
