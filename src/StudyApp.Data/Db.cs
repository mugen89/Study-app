using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Data.Sqlite;

using StudyApp.Domain;

namespace StudyApp.Data;

/// <summary>
/// Thin wrapper over a single SQLite connection (desktop app, one user, UI thread).
/// Parameters are passed as anonymous objects: <c>db.Exec("... WHERE id=@id", new { id })</c>.
/// </summary>
public sealed class Db : IDisposable
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropCache = new();
    private SqliteTransaction? _tx;

    public SqliteConnection Connection { get; }
    public string FilePath { get; }

    public Db(string filePath)
    {
        FilePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        };
        Connection = new SqliteConnection(csb.ToString());
        Connection.Open();

        // Unicode-aware helpers: SQLite's built-in lower()/LIKE only fold ASCII (Cyrillic would not match).
        Connection.CreateFunction("ulower", (string? s) => s?.ToLowerInvariant());

        Exec("PRAGMA journal_mode = WAL;");
        Exec("PRAGMA synchronous = NORMAL;");
        Exec("PRAGMA temp_store = MEMORY;");
        Migrations.Apply(this);
    }

    public bool InTransaction => _tx != null;

    public int Exec(string sql, object? args = null)
    {
        using var cmd = Command(sql, args);
        return cmd.ExecuteNonQuery();
    }

    public long Insert(string sql, object? args = null)
    {
        using var cmd = Command(sql + "; SELECT last_insert_rowid();", args);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public T Scalar<T>(string sql, object? args = null, T fallback = default!)
    {
        using var cmd = Command(sql, args);
        var value = cmd.ExecuteScalar();
        if (value == null || value is DBNull) return fallback;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, object? args = null)
    {
        using var cmd = Command(sql, args);
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read()) list.Add(map(reader));
        return list;
    }

    public T? First<T>(string sql, Func<SqliteDataReader, T> map, object? args = null) where T : class
    {
        using var cmd = Command(sql, args);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    /// <summary>Runs the action in a transaction (nested calls join the outer one).</summary>
    public void Tx(Action action)
    {
        if (_tx != null)
        {
            action();
            return;
        }

        _tx = Connection.BeginTransaction();
        try
        {
            action();
            _tx.Commit();
        }
        catch
        {
            try { _tx.Rollback(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            _tx.Dispose();
            _tx = null;
        }
    }

    public T Tx<T>(Func<T> func)
    {
        T result = default!;
        Tx(() => { result = func(); });
        return result;
    }

    /// <summary>Copies the live database into <paramref name="destinationPath"/> (safe while open).</summary>
    public void BackupTo(string destinationPath)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        var csb = new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false };
        using var dest = new SqliteConnection(csb.ToString());
        dest.Open();
        Connection.BackupDatabase(dest);
    }

    private SqliteCommand Command(string sql, object? args)
    {
        var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
        if (args != null) Bind(cmd, args);
        return cmd;
    }

    private static void Bind(SqliteCommand cmd, object args)
    {
        if (args is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
                cmd.Parameters.AddWithValue(key.StartsWith('@') ? key : "@" + key, ToDb(value));
            return;
        }

        var props = PropCache.GetOrAdd(args.GetType(), t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        foreach (var p in props)
            cmd.Parameters.AddWithValue("@" + p.Name, ToDb(p.GetValue(args)));
    }

    private static object ToDb(object? value) => value switch
    {
        null => DBNull.Value,
        bool b => b ? 1 : 0,
        Enum e => Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => Time.ToMs(dt),
        _ => value,
    };

    public void Dispose()
    {
        try { Exec("PRAGMA optimize;"); } catch { /* ignore */ }
        Connection.Close();
        Connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}

/// <summary>Null-safe column accessors by name.</summary>
public static class ReaderExtensions
{
    public static string Str(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? "" : r.GetString(i);
    }

    public static long Long(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : r.GetInt64(i);
    }

    public static long? LongN(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetInt64(i);
    }

    public static int Int(this SqliteDataReader r, string col) => (int)r.Long(col);

    public static bool Bool(this SqliteDataReader r, string col) => r.Long(col) != 0;

    public static double Dbl(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : r.GetDouble(i);
    }

    public static bool Has(this SqliteDataReader r, string col)
    {
        for (int i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), col, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
