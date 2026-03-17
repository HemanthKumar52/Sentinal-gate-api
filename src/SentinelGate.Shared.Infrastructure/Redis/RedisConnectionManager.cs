using System.Collections.Concurrent;
using StackExchange.Redis;

namespace SentinelGate.Shared.Infrastructure.Redis;

public sealed class RedisConnectionManager : IDisposable
{
    private readonly string _connectionString;
    private ConnectionMultiplexer? _connection;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// In-memory fallback store used when Redis is unavailable.
    /// Keys map to (value, expiry) tuples.
    /// </summary>
    public ConcurrentDictionary<string, (long Value, DateTime Expiry)> InMemoryFallback { get; } = new();

    public bool IsConnected => _connection?.IsConnected == true;

    public RedisConnectionManager(string connectionString)
    {
        _connectionString = connectionString;
        TryConnect();
    }

    public bool TryConnect()
    {
        if (IsConnected)
            return true;

        lock (_lock)
        {
            if (IsConnected)
                return true;

            try
            {
                var options = ConfigurationOptions.Parse(_connectionString);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 5000;
                options.SyncTimeout = 3000;

                _connection = ConnectionMultiplexer.Connect(options);
                return _connection.IsConnected;
            }
            catch
            {
                _connection = null;
                return false;
            }
        }
    }

    public IDatabase? GetDatabase(int db = -1)
    {
        if (IsConnected)
            return _connection!.GetDatabase(db);

        // Attempt reconnect
        if (TryConnect())
            return _connection!.GetDatabase(db);

        return null;
    }

    /// <summary>
    /// Cleans up expired entries from the in-memory fallback dictionary.
    /// </summary>
    public void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;
        foreach (var key in InMemoryFallback.Keys)
        {
            if (InMemoryFallback.TryGetValue(key, out var entry) && entry.Expiry <= now)
            {
                InMemoryFallback.TryRemove(key, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection?.Close();
        _connection?.Dispose();
    }
}
