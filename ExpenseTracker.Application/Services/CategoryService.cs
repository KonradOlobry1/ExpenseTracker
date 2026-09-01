using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;

namespace ExpenseTracker.Application.Services;

public class CategoryService(ICategoryRepository repository) : ICategoryService
{
    public Task<List<Category>> GetAllAsync(CancellationToken ct = default)
        => repository.GetAllAsync(ct);

    public Task<Category> CreateAsync(Category category, CancellationToken ct = default)
        => repository.CreateAsync(category, ct);

    public Task<Category> UpdateAsync(Category category, CancellationToken ct = default)
        => repository.UpdateAsync(category, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => repository.DeleteAsync(id, ct);
}
