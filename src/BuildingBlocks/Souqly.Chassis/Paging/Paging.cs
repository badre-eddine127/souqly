namespace Souqly.Chassis.Paging;

/// <summary>Demande de page : page commence à 0, taille plafonnée à 100.</summary>
public sealed record PageRequest(int Page, int Size)
{
    public const int MaxSize = 100;
    public const int DefaultSize = 20;

    public static PageRequest Of(int? page, int? size) =>
        new(Math.Max(page ?? 0, 0), Math.Clamp(size ?? DefaultSize, 1, MaxSize));

    public int Skip => Page * Size;
}

/// <summary>Réponse paginée normalisée : { content, page, size, totalElements, totalPages }.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Content, int Page, int Size, long TotalElements, int TotalPages)
{
    public static PageResponse<T> From(IReadOnlyList<T> content, PageRequest request, long total) =>
        new(content, request.Page, request.Size, total, (int)Math.Ceiling(total / (double)request.Size));

    public PageResponse<TOut> Map<TOut>(Func<T, TOut> map) =>
        new(Content.Select(map).ToList(), Page, Size, TotalElements, TotalPages);
}
