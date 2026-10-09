using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DJLibrary
{
    internal sealed class WinSqliteDb : IDisposable
    {
        internal const int SQLITE_OK = 0;
        internal const int SQLITE_ROW = 100;
        internal const int SQLITE_DONE = 101;
        private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);
        private IntPtr _db;
        public string Path { get; private set; }

        public WinSqliteDb(string path)
        {
            Path = path;
            int rc = Native.sqlite3_open16(path, out _db);
            if (rc != SQLITE_OK || _db == IntPtr.Zero)
            {
                string message = _db == IntPtr.Zero ? "SQLite konnte nicht geöffnet werden." : ErrorMessage();
                if (_db != IntPtr.Zero) Native.sqlite3_close(_db);
                _db = IntPtr.Zero;
                throw new InvalidOperationException(message);
            }
            Native.sqlite3_busy_timeout(_db, 5000);
        }

        public void Dispose()
        {
            if (_db != IntPtr.Zero)
            {
                Native.sqlite3_close(_db);
                _db = IntPtr.Zero;
            }
        }

        public string ErrorMessage()
        {
            if (_db == IntPtr.Zero) return "SQLite-Verbindung ist geschlossen.";
            IntPtr p = Native.sqlite3_errmsg16(_db);
            return p == IntPtr.Zero ? "Unbekannter SQLite-Error." : Marshal.PtrToStringUni(p);
        }

        private IntPtr Prepare(string sql)
        {
            IntPtr stmt;
            int rc = Native.sqlite3_prepare16_v2(_db, sql, -1, out stmt, IntPtr.Zero);
            if (rc != SQLITE_OK) throw new InvalidOperationException("SQLite prepare: " + ErrorMessage() + "\nSQL: " + sql);
            return stmt;
        }

        private void Bind(IntPtr stmt, object[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                object value = args[i];
                int index = i + 1;
                int rc;
                if (value == null || value == DBNull.Value) rc = Native.sqlite3_bind_null(stmt, index);
                else if (value is bool) rc = Native.sqlite3_bind_int(stmt, index, ((bool)value) ? 1 : 0);
                else if (value is byte || value is short || value is int) rc = Native.sqlite3_bind_int(stmt, index, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                else if (value is long) rc = Native.sqlite3_bind_int64(stmt, index, (long)value);
                else if (value is float || value is double || value is decimal) rc = Native.sqlite3_bind_double(stmt, index, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                else if (value is byte[])
                {
                    byte[] bytes = (byte[])value;
                    GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    try { rc = Native.sqlite3_bind_blob(stmt, index, handle.AddrOfPinnedObject(), bytes.Length, SQLITE_TRANSIENT); }
                    finally { handle.Free(); }
                }
                else rc = Native.sqlite3_bind_text16(stmt, index, Convert.ToString(value, CultureInfo.InvariantCulture), -1, SQLITE_TRANSIENT);
                if (rc != SQLITE_OK) throw new InvalidOperationException("SQLite bind: " + ErrorMessage());
            }
        }

        public void Execute(string sql, params object[] args)
        {
            IntPtr stmt = Prepare(sql);
            try
            {
                Bind(stmt, args);
                int rc = Native.sqlite3_step(stmt);
                while (rc == SQLITE_ROW) rc = Native.sqlite3_step(stmt);
                if (rc != SQLITE_DONE) throw new InvalidOperationException("SQLite execute: " + ErrorMessage() + "\nSQL: " + sql);
            }
            finally { Native.sqlite3_finalize(stmt); }
        }

        public object Scalar(string sql, params object[] args)
        {
            IntPtr stmt = Prepare(sql);
            try
            {
                Bind(stmt, args);
                int rc = Native.sqlite3_step(stmt);
                if (rc == SQLITE_DONE) return null;
                if (rc != SQLITE_ROW) throw new InvalidOperationException("SQLite scalar: " + ErrorMessage() + "\nSQL: " + sql);
                return ColumnValue(stmt, 0);
            }
            finally { Native.sqlite3_finalize(stmt); }
        }

        public List<Dictionary<string, object>> Query(string sql, params object[] args)
        {
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            IntPtr stmt = Prepare(sql);
            try
            {
                Bind(stmt, args);
                int count = Native.sqlite3_column_count(stmt);
                while (true)
                {
                    int rc = Native.sqlite3_step(stmt);
                    if (rc == SQLITE_DONE) break;
                    if (rc != SQLITE_ROW) throw new InvalidOperationException("SQLite query: " + ErrorMessage() + "\nSQL: " + sql);
                    Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr namePtr = Native.sqlite3_column_name16(stmt, i);
                        string name = namePtr == IntPtr.Zero ? i.ToString(CultureInfo.InvariantCulture) : Marshal.PtrToStringUni(namePtr);
                        row[name] = ColumnValue(stmt, i);
                    }
                    rows.Add(row);
                }
                return rows;
            }
            finally { Native.sqlite3_finalize(stmt); }
        }

        private static object ColumnValue(IntPtr stmt, int index)
        {
            int type = Native.sqlite3_column_type(stmt, index);
            if (type == 1) return Native.sqlite3_column_int64(stmt, index);
            if (type == 2) return Native.sqlite3_column_double(stmt, index);
            if (type == 3)
            {
                IntPtr p = Native.sqlite3_column_text16(stmt, index);
                int bytes = Native.sqlite3_column_bytes16(stmt, index);
                return p == IntPtr.Zero ? "" : Marshal.PtrToStringUni(p, bytes / 2);
            }
            if (type == 4)
            {
                int bytes = Native.sqlite3_column_bytes(stmt, index);
                byte[] result = new byte[bytes];
                IntPtr p = Native.sqlite3_column_blob(stmt, index);
                if (p != IntPtr.Zero && bytes > 0) Marshal.Copy(p, result, 0, bytes);
                return result;
            }
            return null;
        }

        public long LastInsertRowId { get { return Native.sqlite3_last_insert_rowid(_db); } }
        public int Changes { get { return Native.sqlite3_changes(_db); } }

        public void Transaction(Action body)
        {
            Execute("BEGIN IMMEDIATE");
            try
            {
                body();
                Execute("COMMIT");
            }
            catch
            {
                try { Execute("ROLLBACK"); } catch { }
                throw;
            }
        }

        public string QuickCheck()
        {
            object value = Scalar("PRAGMA quick_check");
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public void BackupTo(string destination)
        {
            using (WinSqliteDb output = new WinSqliteDb(destination))
            {
                IntPtr backup = Native.sqlite3_backup_init(output._db, "main", _db, "main");
                if (backup == IntPtr.Zero) throw new InvalidOperationException("SQLite backup init: " + output.ErrorMessage());
                int rc;
                try { rc = Native.sqlite3_backup_step(backup, -1); }
                finally
                {
                    int finish = Native.sqlite3_backup_finish(backup);
                    if (finish != SQLITE_OK && finish != SQLITE_DONE) throw new InvalidOperationException("SQLite backup finish: " + output.ErrorMessage());
                }
                if (rc != SQLITE_DONE) throw new InvalidOperationException("SQLite backup step: " + output.ErrorMessage());
            }
        }

        private static class Native
        {
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            internal static extern int sqlite3_open16(string filename, out IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_errmsg16(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            internal static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int nByte, out IntPtr stmt, IntPtr tail);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_step(IntPtr stmt);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_null(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_int(IntPtr stmt, int index, int value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
            internal static extern int sqlite3_bind_text16(IntPtr stmt, int index, string value, int bytes, IntPtr destructor);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_bind_blob(IntPtr stmt, int index, IntPtr value, int bytes, IntPtr destructor);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_name16(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_type(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern long sqlite3_column_int64(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern double sqlite3_column_double(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_text16(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_bytes16(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_blob(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_bytes(IntPtr stmt, int index);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern long sqlite3_last_insert_rowid(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_changes(IntPtr db);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern IntPtr sqlite3_backup_init(IntPtr destinationDb, string destinationName, IntPtr sourceDb, string sourceName);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_backup_step(IntPtr backup, int pages);
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_backup_finish(IntPtr backup);
        }
    }
}
