namespace ExpenseTracker.Contracts;

// The wire contract for sync, shared by the API, the device client and the tests.
//
// It used to be declared three times — once as API DTOs, once as private records inside the
// device SyncService, and once more by hand in the test helpers. Three copies of one contract
// drift silently: adding a field to the server compiles perfectly well against a client that
// has never heard of it, and the failure only appears at run time as a silently missing value.
// One definition means that change stops compiling until every side has handled it.
//
// Deliberately separate from the entities. Binding EF entities straight from a request would
// let a client set Id or UserId (over-posting), and [ApiController] treats their non-nullable
// navigation properties as required, which rejects every payload. Sharing one persistence
// model across the device and the cloud is the win; sharing the serialization model is not.

public record SyncExpenseDto(
    Guid SyncId, string Description, decimal Amount, DateTime Date,
    Guid CategorySyncId, string? Notes,
    DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

public record SyncIncomeDto(
    Guid SyncId, string Description, decimal Amount, string BillingCycle,
    DateTime StartDate, string? Notes, bool IsActive,
    DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

public record SyncSubscriptionDto(
    Guid SyncId, string Name, decimal Amount, string BillingCycle,
    DateTime StartDate, DateTime? EndDate, Guid CategorySyncId,
    string? Notes, string? Url, bool IsActive,
    DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

public record SyncCategoryDto(
    Guid SyncId, string Name, string Icon, string Color, bool IsSystem,
    DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

/// <summary>
/// Account-wide display preferences. Not a row, so it carries no SyncId — there is exactly
/// one per account and it is replaced wholesale rather than merged field by field.
/// </summary>
public record SyncSettingsDto(
    string Currency, string Language, bool IsDarkMode, DateTime UpdatedAt);

public record SyncPushRequest(
    List<SyncExpenseDto>? Expenses = null,
    List<SyncIncomeDto>? Incomes = null,
    List<SyncSubscriptionDto>? Subscriptions = null,
    List<SyncCategoryDto>? Categories = null,
    SyncSettingsDto? Settings = null);

public record SyncPullResponse(
    List<SyncExpenseDto>? Expenses,
    List<SyncIncomeDto>? Incomes,
    List<SyncSubscriptionDto>? Subscriptions,
    List<SyncCategoryDto>? Categories,
    DateTime ServerTime,
    SyncSettingsDto? Settings = null);
