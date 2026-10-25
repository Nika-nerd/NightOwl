using System.Text.Json;
using Microsoft.Data.Sqlite;
using NightOwl.Core;

namespace NightOwl.Pipeline.Caching;

public sealed class SqliteCacheStore : ICacheStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SqliteCacheStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS cache (
                    key        TEXT PRIMARY KEY,
                    value      TEXT NOT NULL,
                    expires_at INTEGER NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();

            using var purge = conn.CreateCommand();
            purge.CommandText = "DELETE FROM cache WHERE expires_at < $now";
            purge.Parameters.AddWithValue("$now", Now());
            purge.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // кэш не критичен: дальше Get/Set тоже будут молча пропускаться
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM cache WHERE key = $key AND expires_at > $now";
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$now", Now());

            var value = await cmd.ExecuteScalarAsync(ct) as string;
            return value is null ? null : JsonSerializer.Deserialize<T>(value, Json);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or IOException)
        {
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO cache (key, value, expires_at) VALUES ($key, $value, $expires)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value, expires_at = excluded.expires_at
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value, Json));
            cmd.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds());

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or IOException)
        {
            // не удалось записать в кэш: не страшно
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}