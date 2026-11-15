using Microsoft.Data.Sqlite;

namespace DaiseyTracker;

/// <summary>An event: start (unix ms), duration (s), data (JSON).</summary>
record Ev(long Ts, double Dur, string Data)
{
    public long End => Ts + (long)(Dur * 1000);
}

/// <summary>SQLite heartbeat store (ActivityWatch-style merge).</summary>
sealed class Store : IDisposable
{
    const double DefaultPulse = 3.0; // seconds; max gap that still merges into last event
    const long MaxLookbackMs = 48L * 3600 * 1000; // max length an event may have for Query

    sealed class Last { public long Id; public long Ts; public double Dur; public string Data = ""; }

    readonly SqliteConnection _db;
    readonly SqliteCommand _ins, _upd;
    readonly Dictionary<string, Last> _last = new();
    readonly object _sync = new(); // dashboard thread reads while UI thread writes

    public Store(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        using (var c = _db.CreateCommand())
        {
            c.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS events(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  stream TEXT NOT NULL,
  ts INTEGER NOT NULL,
  duration REAL NOT NULL,
  data TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_events_stream_ts ON events(stream, ts);";
            c.ExecuteNonQuery();
        }

        _ins = _db.CreateCommand();
        _ins.CommandText = "INSERT INTO events(stream,ts,duration,data) VALUES($s,$t,0,$d); SELECT last_insert_rowid();";
        _ins.Parameters.Add("$s", SqliteType.Text);
        _ins.Parameters.Add("$t", SqliteType.Integer);
        _ins.Parameters.Add("$d", SqliteType.Text);

        _upd = _db.CreateCommand();
        _upd.CommandText = "UPDATE events SET duration=$u WHERE id=$i";
        _upd.Parameters.Add("$u", SqliteType.Real);
        _upd.Parameters.Add("$i", SqliteType.Integer);
    }

    /// <summary>
    /// Same data as last event of the stream and gap &lt;= pulse: extend its duration. Else new row.
    /// Audio can have several processes at once, so its "last event" is tracked per data value.
    /// </summary>
    public void Heartbeat(string stream, long tsMs, string data, double pulse = DefaultPulse)
    {
        lock (_sync) HeartbeatLocked(stream, tsMs, data, pulse);
    }

    void HeartbeatLocked(string stream, long tsMs, string data, double pulse)
    {
        var key = stream == "audio" ? stream + "|" + data : stream;
        if (_last.TryGetValue(key, out var l) && l.Data == data && tsMs >= l.Ts)
        {
            var dur = (tsMs - l.Ts) / 1000.0;
            if (dur - l.Dur <= pulse)
            {
                _upd.Parameters["$u"].Value = dur;
                _upd.Parameters["$i"].Value = l.Id;
                _upd.ExecuteNonQuery();
                l.Dur = dur;
                return;
            }
        }

        _ins.Parameters["$s"].Value = stream;
        _ins.Parameters["$t"].Value = tsMs;
        _ins.Parameters["$d"].Value = data;
        var id = (long)_ins.ExecuteScalar()!;
        _last[key] = new Last { Id = id, Ts = tsMs, Dur = 0, Data = data };
    }

    /// <summary>Events of a stream overlapping [fromMs, toMs), ordered by start.</summary>
    public List<Ev> Query(string stream, long fromMs, long toMs)
    {
        lock (_sync) return QueryLocked(stream, fromMs, toMs);
    }

    List<Ev> QueryLocked(string stream, long fromMs, long toMs)
    {
        var list = new List<Ev>();
        using var c = _db.CreateCommand();
        c.CommandText = @"SELECT ts,duration,data FROM events
WHERE stream=$s AND ts>=$lo AND ts<$to AND ts+duration*1000>$from ORDER BY ts";
        c.Parameters.AddWithValue("$s", stream);
        c.Parameters.AddWithValue("$lo", fromMs - MaxLookbackMs);
        c.Parameters.AddWithValue("$to", toMs);
        c.Parameters.AddWithValue("$from", fromMs);
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(new Ev(r.GetInt64(0), r.GetDouble(1), r.GetString(2)));
        return list;
    }

    /// <summary>Delete raw events older than <paramref name="days"/> days. Daily summaries are kept.</summary>
    public int Prune(int days)
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();
        lock (_sync)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "DELETE FROM events WHERE ts + duration*1000 < $c";
            c.Parameters.AddWithValue("$c", cutoff);
            return c.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        lock (_sync) { DisposeLocked(); }
    }

    void DisposeLocked()
    {
        _ins.Dispose();
        _upd.Dispose();
        _db.Dispose();
    }
}
