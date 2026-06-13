namespace Zentory.Application.Common.Models;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              Total,
    int              Page,
    int              PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
    public bool HasNext   => Page < TotalPages;
}
