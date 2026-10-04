using Themia.App.Services.Accounts;
using Themia.Core.Accounts;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services.Files;

/// <summary>A Microsoft account that can feed the OneDrive widget.</summary>
/// <param name="Problem">Why it cannot be used until the user signs in again, or null.</param>
internal sealed record DriveAccount(string Id, string Label, string? Problem);

/// <summary>
/// Where drive listings come from: Themia's own Microsoft accounts, or the gallery's fixture.
/// </summary>
internal interface IDriveRemote
{
    IReadOnlyList<DriveAccount> Accounts { get; }

    /// <summary>Raised on the UI thread when an account is added, removed or changes health.</summary>
    event Action? AccountsChanged;

    Task<IReadOnlyList<DriveItem>> ListAsync(string accountId, Uri address, int max, CancellationToken cancellation);
}

/// <summary>[S-607] OneDrive through <see cref="AccountService"/>: Graph pages, typed failures.</summary>
internal sealed class AccountDriveRemote(AccountService accounts) : IDriveRemote
{
    public IReadOnlyList<DriveAccount> Accounts =>
    [
        .. accounts.WithFeature(AccountFeatures.Files)
            .Where(a => a.Provider == AccountProvider.Microsoft)
            .Select(a => new DriveAccount(a.Id, a.Label, a.Problem)),
    ];

    public event Action? AccountsChanged
    {
        add => accounts.AccountsChanged += value;
        remove => accounts.AccountsChanged -= value;
    }

    public async Task<IReadOnlyList<DriveItem>> ListAsync(string accountId, Uri address, int max, CancellationToken cancellation)
    {
        // $top already asks for this many; the cap stops a next link from turning one view into
        // fifty pages.
        var items = await accounts.GetGraphPagesAsync(accountId, address, DriveWireContext.Default.GraphDriveItem, max, cancellation)
            .ConfigureAwait(false);
        return [.. items.Select(DriveRules.FromGraph).OfType<DriveItem>()];
    }
}

/// <summary>
/// [S-607] Drive listings for every OneDrive widget, from one place. Single-flight per address: two
/// widgets showing the same folder, or one widget's tick landing on a navigation, make one request.
/// A listing younger than the caller's allowance is served from memory. One instance, reached
/// through <see cref="AppServices.Drive"/>.
/// </summary>
internal sealed class DriveService : IDisposable
{
    private const string Scope = "onedrive";

    private readonly IDriveRemote _remote;
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<DriveItem> Items)> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<IReadOnlyList<DriveItem>>> _inFlight = new(StringComparer.Ordinal);
    private bool _disposed;

    public DriveService(IDriveRemote remote)
    {
        _remote = remote;
        _remote.AccountsChanged += OnAccountsChanged;
    }

    /// <summary>Raised on the UI thread when the Microsoft accounts changed.</summary>
    public event Action? AccountsChanged;

    public IReadOnlyList<DriveAccount> Accounts => _remote.Accounts;

    public DriveAccount? Find(string? accountId)
        => string.IsNullOrEmpty(accountId) ? null : Accounts.FirstOrDefault(a => a.Id == accountId);

    /// <summary>
    /// A listing: from memory when it is younger than <paramref name="maxAge"/>, else from Graph,
    /// joining a request for the same address already out.
    /// </summary>
    /// <exception cref="AccountException">How the request failed.</exception>
    public Task<IReadOnlyList<DriveItem>> ListAsync(string accountId, Uri address, int max, TimeSpan maxAge)
    {
        var key = $"{accountId}\n{address.AbsoluteUri}";

        if (_cache.TryGetValue(key, out var held) && DateTimeOffset.Now - held.At < maxAge)
        {
            return Task.FromResult(held.Items);
        }

        if (_inFlight.TryGetValue(key, out var running))
        {
            return running;
        }

        var load = LoadAsync(key, accountId, address, max);

        // A source that answers synchronously (the gallery's) has already finished, and its own
        // clean-up ran before this line: holding it here would serve that answer forever.
        if (!load.IsCompleted)
        {
            _inFlight[key] = load;
        }

        return load;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _remote.AccountsChanged -= OnAccountsChanged;
        AccountsChanged = null;
        _cache.Clear();
    }

    private async Task<IReadOnlyList<DriveItem>> LoadAsync(string key, string accountId, Uri address, int max)
    {
        try
        {
            var items = await _remote.ListAsync(accountId, address, max, CancellationToken.None).ConfigureAwait(true);
            _cache[key] = (DateTimeOffset.Now, items);
            return items;
        }
        finally
        {
            _inFlight.Remove(key);
        }
    }

    private void OnAccountsChanged()
    {
        // A signed-out account's files must not linger in memory.
        _cache.Clear();

        if (AccountsChanged is not { } changed)
        {
            return;
        }

        foreach (var listener in changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                listener();
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "an accounts listener threw", ex);
            }
        }
    }
}
