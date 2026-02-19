using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Infrastructure.Repositories;

#region Interfaces

/// <summary>
/// Generic async repository contract.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IRepository<T> where T : class, IEntity
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IEntity
{
    int Id { get; }
    DateTime CreatedAt { get; }
}

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> SearchAsync(UserSearchFilter filter, CancellationToken ct = default);
    Task<PagedResult<User>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}

#endregion

#region Entities & Value Objects

public enum UserRole
{
    Guest,
    Member,
    Admin,
    SuperAdmin
}

public record Address(string Street, string City, string Country, string PostalCode);

public sealed class User : IEntity
{
    public int Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public Address? Address { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public IReadOnlyList<string> Tags => _tags.AsReadOnly();

    private readonly List<string> _tags = new();

    private User() { } // EF constructor

    public User(string email, string displayName, UserRole role = UserRole.Member)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Role = role;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateAddress(Address address) => Address = address;
    public void AddTag(string tag) => _tags.Add(tag);
    public void Deactivate() => IsActive = false;

    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
    }

    public bool HasRole(UserRole role) => Role >= role;
}

#endregion

#region DTOs & Filters

public record UserSearchFilter(
    string? EmailContains = null,
    string? NameContains = null,
    UserRole? Role = null,
    bool? IsActive = null,
    DateTime? CreatedAfter = null
);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

#endregion

#region Repository Implementation

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(AppDbContext context, ILogger<UserRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user by ID {UserId}", id);
            throw;
        }
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Users
            .AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<User>> SearchAsync(
        UserSearchFilter filter,
        CancellationToken ct = default)
    {
        var query = _context.Users.AsNoTracking().AsQueryable();

        if (filter.EmailContains is not null)
            query = query.Where(u => u.Email.Contains(filter.EmailContains));

        if (filter.NameContains is not null)
            query = query.Where(u => u.DisplayName.Contains(filter.NameContains));

        if (filter.Role is not null)
            query = query.Where(u => u.Role == filter.Role);

        if (filter.IsActive is not null)
            query = query.Where(u => u.IsActive == filter.IsActive);

        if (filter.CreatedAfter is not null)
            query = query.Where(u => u.CreatedAt >= filter.CreatedAfter);

        return await query.OrderBy(u => u.Email).ToListAsync(ct);
    }

    public async Task<PagedResult<User>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var total = await _context.Users.CountAsync(ct);

        var items = await _context.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<User>(items, total, page, pageSize);
    }

    public async Task<User> AddAsync(User entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await _context.Users.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created user {Email}", entity.Email);
        return entity;
    }

    public async Task UpdateAsync(User entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        _context.Users.Update(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.Users.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"User {id} not found.");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted user {UserId}", id);
    }
}

#endregion

#region Service Layer

public sealed class UserService
{
    private readonly IUserRepository _repo;
    private readonly ILogger<UserService> _logger;

    // Demonstrates: lambda, null-coalescing, string interpolation, ternary
    private static string NormalizeEmail(string email) =>
        email?.Trim().ToLowerInvariant()
            ?? throw new ArgumentNullException(nameof(email));

    public UserService(IUserRepository repo, ILogger<UserService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<User> CreateUserAsync(
        string email,
        string displayName,
        UserRole role,
        CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);

        var existing = await _repo.GetByEmailAsync(normalized, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Email '{normalized}' already registered.");

        var user = new User(normalized, displayName, role);
        return await _repo.AddAsync(user, ct);
    }

    // Demonstrates: LINQ query syntax, pattern matching, switch expression
    public async Task<Dictionary<UserRole, int>> GetRoleStatisticsAsync(CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);

        return all
            .GroupBy(u => u.Role)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public string DescribeRole(UserRole role) => role switch
    {
        UserRole.Guest      => "Read-only access",
        UserRole.Member     => "Standard access",
        UserRole.Admin      => "Full management access",
        UserRole.SuperAdmin => "Unrestricted system access",
        _                   => throw new ArgumentOutOfRangeException(nameof(role))
    };

    // Demonstrates: async streams, yield return
    public async IAsyncEnumerable<User> StreamActiveUsersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var page = 1;
        const int pageSize = 50;

        while (true)
        {
            var result = await _repo.GetPagedAsync(page, pageSize, ct);

            foreach (var user in result.Items.Where(u => u.IsActive))
                yield return user;

            if (!result.HasNextPage) break;
            page++;
        }
    }

    // Demonstrates: deconstruct, tuple, is pattern, null-conditional
    public async Task<(bool Found, User? User, string Message)> TryGetUserAsync(
        int id,
        CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(id, ct);

        return user is { IsActive: true }
            ? (true, user, $"Found active user {user.DisplayName}")
            : (false, null, user is null ? "Not found" : "User is inactive");
    }

    // Demonstrates: preprocessor, unsafe, checked
    public static int SafeAdd(int a, int b)
    {
#if DEBUG
        Console.WriteLine($"SafeAdd called: {a} + {b}");
#endif
        checked
        {
            return a + b;
        }
    }
}

#endregion

#region Stubs (compile targets)

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
}

public interface ILogger<T>
{
    void LogInformation(string msg, params object[] args);
    void LogError(Exception ex, string msg, params object[] args);
}

#endregion