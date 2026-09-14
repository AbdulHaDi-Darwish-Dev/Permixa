namespace Permixa.Application.Common.Paging;

/// <summary>
/// Bounded 1-based page request. Maximum page size is 100.
/// </summary>
public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public bool IsValid => Page >= 1 && PageSize >= 1 && PageSize <= MaxPageSize;
}
