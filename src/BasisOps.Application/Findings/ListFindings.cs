using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Systems;

namespace BasisOps.Application.Findings;

public sealed record ListFindingsQuery(string? Sid, Severity? Severity, bool OpenOnly = true, int Page = 1, int PageSize = 50);

public sealed class ListFindingsHandler(IFindingRepository findings) : IQueryHandler<ListFindingsQuery, PagedResult<FindingDto>>
{
    public const int MaxPageSize = 200;

    public async Task<Result<PagedResult<FindingDto>>> HandleAsync(ListFindingsQuery query, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (query.Sid is not null && !SapSystem.IsValidSid(query.Sid))
        {
            errors.Add("sid", "SID must be three uppercase letters or digits.");
        }

        if (query.Page < 1)
        {
            errors.Add("page", "Page must be 1 or greater.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var page = await findings.ListAsync(new FindingFilter(query.Sid, query.Severity, query.OpenOnly), query.Page, query.PageSize, cancellationToken);
        return new PagedResult<FindingDto>([.. page.Items.Select(FindingDto.From)], page.Page, page.PageSize, page.TotalCount);
    }
}
