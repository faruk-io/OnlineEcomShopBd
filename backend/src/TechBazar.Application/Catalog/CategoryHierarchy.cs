using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;

namespace TechBazar.Application.Catalog;

/// <summary>Resolves category ancestry/descendants (the category table is small, so the tree is walked in memory).</summary>
public interface ICategoryHierarchy
{
    /// <summary>Ids of the category with <paramref name="slug"/> plus all descendants; empty when the slug is unknown.</summary>
    Task<IReadOnlyList<int>> GetSelfAndDescendantIdsAsync(string slug, CancellationToken ct = default);
    /// <summary>Root-to-leaf path ending at <paramref name="categoryId"/>.</summary>
    Task<IReadOnlyList<CategoryRefDto>> GetPathAsync(int categoryId, CancellationToken ct = default);
}

public sealed class CategoryHierarchy(IApplicationDbContext db) : ICategoryHierarchy
{
    private sealed record Node(int Id, int? ParentId, string Name, string Slug);

    private Task<List<Node>> LoadAsync(CancellationToken ct) =>
        db.Categories.AsNoTracking().Where(c => c.IsActive)
            .Select(c => new Node(c.Id, c.ParentId, c.Name, c.Slug)).ToListAsync(ct);

    public async Task<IReadOnlyList<int>> GetSelfAndDescendantIdsAsync(string slug, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        var root = all.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (root is null) return [];

        var byParent = all.Where(c => c.ParentId.HasValue).ToLookup(c => c.ParentId!.Value);
        var result = new List<int>();
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            result.Add(n.Id);
            foreach (var child in byParent[n.Id]) stack.Push(child);
        }
        return result;
    }

    public async Task<IReadOnlyList<CategoryRefDto>> GetPathAsync(int categoryId, CancellationToken ct = default)
    {
        var all = (await LoadAsync(ct)).ToDictionary(c => c.Id);
        var path = new List<CategoryRefDto>();
        var guard = 0;
        for (var cur = all.GetValueOrDefault(categoryId); cur is not null && guard++ < 16;
             cur = cur.ParentId is { } pid ? all.GetValueOrDefault(pid) : null)
            path.Insert(0, new CategoryRefDto(cur.Id, cur.Name, cur.Slug));

        return path.Count > 0 ? path : throw new NotFoundException("Category not found.");
    }
}
