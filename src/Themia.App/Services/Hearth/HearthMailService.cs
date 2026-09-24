using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Themia.App.Interop;
using Themia.Core.Diagnostics;

namespace Themia.App.Services.Hearth;

/// <summary>Why the mail widget is showing what it is showing.</summary>
internal enum HearthStatus
{
    Idle,
    Connecting,

    /// <summary>Connected and consented; records are live.</summary>
    Ready,

    /// <summary>The hub is not running on this machine.</summary>
    NotRunning,

    /// <summary>The hub is there but refused us, or the user declined consent.</summary>
    Denied,

    /// <summary>Something else went wrong; see the log.</summary>
    Failed,
}

/// <summary>
/// The app's one connection to the hearth hub.
/// <para>
/// Shared by every mail widget, like the ticker: hearth identifies an app by its process, consent is
/// granted once to that process, and a second connection would mean a second consent prompt for the
/// same app. So there is exactly one, and widgets ask it for what they need.
/// </para>
/// <para>
/// Read-only by design: the scope asked for is <c>email</c> alone, never <c>email.send</c>, so the
/// worst this widget can do with a granted mailbox is show it.
/// </para>
/// </summary>
internal sealed class HearthMailService : IAsyncDisposable
{
    private const string Scope = "hearth";
    private const string HubImageName = "hearth.exe";

    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly Lock _pendingGate = new();

    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readLoop;
    private long _nextId;
    private bool _disposed;

    public HearthMailService(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public HearthStatus Status { get; private set; } = HearthStatus.Idle;

    /// <summary>Mail collections the hub has granted, in the order it listed them.</summary>
    public IReadOnlyList<HearthCollection> MailCollections { get; private set; } = [];

    /// <summary>Raised on the UI thread when mail changed, or when the connection state changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Connect, say hello and subscribe. Safe to call repeatedly: one attempt runs at a time and a
    /// live connection short-circuits.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        if (_disposed)
        {
            return false;
        }

        if (Status == HearthStatus.Ready && _pipe?.IsConnected == true)
        {
            return true;
        }

        await _connectGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (Status == HearthStatus.Ready && _pipe?.IsConnected == true)
            {
                return true;
            }

            return await ConnectAsync().ConfigureAwait(true);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task<bool> ConnectAsync()
    {
        SetStatus(HearthStatus.Connecting);
        Teardown();

        var pipeName = PipeName();

        try
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            // Short: the hub either exists or it does not, and a widget must not hang the surface
            // waiting for one. The hub is deliberately NOT started from here — launching another
            // app's executable is not this widget's business.
            await pipe.ConnectAsync(1500).ConfigureAwait(true);

            if (!VerifyServerIsHub(pipe))
            {
                // The hub is what draws the consent prompt, so talking to an impostor would mean
                // consenting to nothing. Refuse rather than hand it a hello.
                pipe.Dispose();
                SetStatus(HearthStatus.Failed);
                return false;
            }

            _pipe = pipe;
            _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            _readLoop = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(pipe, _readLoop.Token));

            // The hub does not answer hello until the user has answered its consent prompt, so
            // this waits at human speed rather than the 30s an ordinary call gets. A prompt sitting
            // on screen is not a hung hub.
            var hello = await CallAsync(
                "hello",
                new HelloParams { Name = "Themia", Scopes = ["email"] },
                HearthJsonContext.Default.HearthRequestHelloParams,
                TimeSpan.FromMinutes(5)).ConfigureAwait(true);

            var result = hello.Deserialize(HearthJsonContext.Default.HelloResult);
            MailCollections = result?.Collections?.Where(c => c.Type == "email").ToList() ?? [];

            await CallAsync(
                "subscribe",
                new SubscribeParams { Types = ["email"] },
                HearthJsonContext.Default.HearthRequestSubscribeParams).ConfigureAwait(true);

            Log.Info(Scope, $"connected; {MailCollections.Count} mail collection(s) granted");
            SetStatus(HearthStatus.Ready);
            return true;
        }
        catch (TimeoutException)
        {
            SetStatus(HearthStatus.NotRunning);
            return false;
        }
        catch (IOException ex)
        {
            Log.Warn(Scope, "the hub connection dropped while connecting", ex);
            SetStatus(HearthStatus.NotRunning);
            return false;
        }
        catch (HearthCallException ex)
        {
            // "denied" is the user declining consent, which is an answer, not a fault.
            Log.Info(Scope, $"the hub refused the connection: {ex.Message}");
            SetStatus(ex.Code == "denied" ? HearthStatus.Denied : HearthStatus.Failed);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not connect to the hub", ex);
            SetStatus(HearthStatus.Failed);
            return false;
        }
    }

    /// <summary>
    /// Recent messages. Returns an empty list rather than throwing, because this runs on a render
    /// path and an unreachable hub is an ordinary state, not an error.
    /// </summary>
    public async Task<IReadOnlyList<EmailRecord>> RecentAsync(string? collectionId, bool unreadOnly, int limit)
    {
        if (!await EnsureConnectedAsync().ConfigureAwait(true))
        {
            return [];
        }

        try
        {
            var query = new EmailQueryParams
            {
                Collection = string.IsNullOrWhiteSpace(collectionId) ? null : collectionId,
                Filter = unreadOnly ? new EmailFilter { Unread = true } : null,
                Sort = [new SortSpec { By = "date", Desc = true }],
                Limit = Math.Clamp(limit, 1, 50),
            };

            var raw = await CallAsync(
                "email.query",
                query,
                HearthJsonContext.Default.HearthRequestEmailQueryParams).ConfigureAwait(true);

            var result = raw.Deserialize(HearthJsonContext.Default.EmailQueryResult);
            var records = result?.Records ?? [];

            // Hearth sorts by the Date header; a list reads better ordered by when the message
            // actually arrived, which is what its timestamps show.
            return records.OrderByDescending(r => r.When).ToList();
        }
        catch (HearthCallException ex)
        {
            Log.Warn(Scope, $"email.query refused: {ex.Message}");
            SetStatus(ex.Code == "denied" ? HearthStatus.Denied : HearthStatus.Failed);
            return [];
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "email.query failed", ex);
            SetStatus(HearthStatus.Failed);
            return [];
        }
    }

    // -----------------------------------------------------------------------------------------
    // Transport
    // -----------------------------------------------------------------------------------------

    private async Task<JsonElement> CallAsync<T>(
        string method,
        T parameters,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<HearthRequest<T>> typeInfo,
        TimeSpan? timeout = null)
    {
        var writer = _writer ?? throw new IOException("not connected to the hub");
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingGate)
        {
            _pending[id] = completion;
        }

        var line = JsonSerializer.Serialize(
            new HearthRequest<T> { Id = id, Method = method, Params = parameters },
            typeInfo);

        await writer.WriteAsync(line + "\n").ConfigureAwait(false);

        // A hub that never answers must not leave a widget waiting forever.
        var finished = await Task.WhenAny(
            completion.Task,
            Task.Delay(timeout ?? TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        if (finished != completion.Task)
        {
            lock (_pendingGate)
            {
                _pending.Remove(id);
            }

            throw new TimeoutException($"the hub did not answer '{method}'");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break; // the hub closed the pipe
                }

                if (line.Length != 0)
                {
                    Dispatch(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore: teardown cancels this deliberately
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "the hub connection dropped", ex);
        }

        if (!token.IsCancellationRequested)
        {
            FailPending(new IOException("the hub connection dropped"));
            _dispatcher.TryEnqueue(() => SetStatus(HearthStatus.NotRunning));
        }
    }

    private void Dispatch(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            // An event carries no id and is never a reply. It carries no data either: it is a nudge
            // to re-query, which is why this only raises Changed.
            if (root.TryGetProperty("event", out _))
            {
                _dispatcher.TryEnqueue(() => Changed?.Invoke());
                return;
            }

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
            {
                return;
            }

            TaskCompletionSource<JsonElement>? completion;
            lock (_pendingGate)
            {
                _pending.Remove(id, out completion);
            }

            if (completion is null)
            {
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                var body = error.Deserialize(HearthJsonContext.Default.HearthErrorBody);
                completion.TrySetException(new HearthCallException(
                    body?.Code ?? "unknown",
                    body?.Message ?? "the hub returned an error"));
                return;
            }

            completion.TrySetResult(root.TryGetProperty("result", out var result)
                ? result.Clone() // the document is disposed below; the element must outlive it
                : default);
        }
        catch (JsonException ex)
        {
            Log.Warn(Scope, "the hub sent a line that is not valid JSON", ex);
        }
    }

    private void FailPending(Exception error)
    {
        TaskCompletionSource<JsonElement>[] waiting;
        lock (_pendingGate)
        {
            waiting = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var completion in waiting)
        {
            completion.TrySetException(error);
        }
    }

    private void SetStatus(HearthStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        Changed?.Invoke();
    }

    /// <summary>
    /// <c>hearth-&lt;user SID&gt;</c>, with the development suffix when <c>HEARTH_DATA_DIR</c> is
    /// set, so a development hub and the real one are never confused for each other.
    /// </summary>
    private static string PipeName()
    {
        string user;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            user = identity.User?.Value ?? Environment.UserName;
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not read the current user's SID", ex);
            user = Environment.UserName;
        }

        var dataDir = Environment.GetEnvironmentVariable("HEARTH_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            return $"hearth-{user}";
        }

        // Hearth hashes the variable as it stands rather than resolving it, so this must too.
        var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(dataDir));
        return $"hearth-{user}-{Convert.ToHexStringLower(digest.AsSpan(0, 4))}";
    }

    /// <summary>
    /// Check the process on the other end is really the hub before saying hello, per hearth's
    /// protocol: the hub draws the consent prompt, so consenting to an impostor would mean
    /// consenting to nothing.
    /// </summary>
    private static bool VerifyServerIsHub(NamedPipeClientStream pipe)
    {
        try
        {
            if (!Win32.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid == 0)
            {
                Log.Error(Scope, "could not identify the pipe's server process; refusing to connect");
                return false;
            }

            using var server = System.Diagnostics.Process.GetProcessById((int)pid);
            var image = server.ProcessName + ".exe";

            if (!image.Equals(HubImageName, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error(Scope, $"the pipe is served by '{image}', not {HubImageName}; refusing to connect");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(Scope, "could not verify the pipe's server process; refusing to connect", ex);
            return false;
        }
    }

    private void Teardown()
    {
        _readLoop?.Cancel();
        _readLoop?.Dispose();
        _readLoop = null;

        FailPending(new IOException("the hub connection was closed"));

        _writer?.Dispose();
        _writer = null;
        _pipe?.Dispose();
        _pipe = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Teardown();
        _connectGate.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
