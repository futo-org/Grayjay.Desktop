using Dapper;
using Grayjay.ClientServer.Database.Indexes;
using Microsoft.Data.Sqlite;
using System.Reflection;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Database
{
    public class DatabaseConnection
    {
        private const int BusyTimeoutMs = 5_000;
        private const int RetryDelayMs = 100;
        private string _connectionString;

        public DatabaseConnection()
        {
            var dbPath = Path.Combine(StateApp.GetAppDirectory().FullName, "database.db");
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                Mode = SqliteOpenMode.ReadWriteCreate
            };
            _connectionString = builder.ToString();
            using var first = new SqliteConnection(_connectionString);
            first.Open();
            InitializeDatabase(first);
        }

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA busy_timeout={BusyTimeoutMs};";
            cmd.ExecuteNonQuery();
            return conn;
        }

        private static T ExecuteWithRetry<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_BUSY)
            {
                Logger.Warning<DatabaseConnection>("SQLITE_BUSY, retrying once after brief delay.", ex);
                Thread.Sleep(RetryDelayMs);
                return action();
            }
        }

        public T SQL<T>(Func<SqliteConnection, T> handle)
        {
            using var conn = OpenConnection();
            return ExecuteWithRetry(() => handle(conn));
        }

        public void EnsureTable<T>(string table, string primaryKey = "ID", bool autoIncrement = true)
        {
            var properties = typeof(T).GetProperties()
                .Where(x => x.GetCustomAttribute<IgnoreAttribute>() == null);
            var propertiesSQL = properties
                .Select(x => (x.Name, GetSQLType(x.PropertyType)))
                .ToArray();

            var tq = Quote(table);
            var result = SQL((x) => x.QueryFirstOrDefault($"SELECT name FROM sqlite_master WHERE type='table' AND name={tq};"));

            if(result != null)
            {
                try
                {
                    var columns = SQL((x) => x.Query($"PRAGMA table_info({tq})"));
                    if (columns.Count() != propertiesSQL.Length)
                    {
                        //Broken, mismatch
                        result = null;
                    }
                    else
                    {
                        foreach (var prop in propertiesSQL)
                        {
                            var column = columns.FirstOrDefault(x => x.name == prop.Name);
                            if (column == null)
                                throw new InvalidDataException($"Missing column {prop.Name}");
                            else if (!string.Equals(column.type, prop.Item2, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException($"Incorrect column type {prop.Item2} = {column.type}");
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error<DatabaseConnection>("Deleting table because broken: " + ex.Message);
                    SQL((x) => x.Execute($"DROP TABLE {tq}"));
                    result = null;
                }
            }

            if(result == null)
            {
                List<string> definitions = new List<string>();
                foreach(var prop in properties) 
                {
                    string field = $"\"{prop.Name}\" {GetSQLType(prop.PropertyType)}";
                    if (prop.Name == primaryKey)
                    {
                        field += " PRIMARY KEY";
                        if (autoIncrement)
                            field += " AUTOINCREMENT";
                    }
                    definitions.Add(field);
                }
                string tableQuery = $"CREATE TABLE {tq}\n({string.Join(",\n", definitions)})";
                SQL((x) => x.Execute(tableQuery));
            }
        }
        private string GetSQLType(Type type)
        {
            if (type == typeof(string))
                return "TEXT";
            else if (type == typeof(int))
                return "INTEGER";
            else if (type == typeof(long))
                return "INTEGER";
            else if (type == typeof(byte[]))
                return "BLOB";
            else if (type == typeof(DateTime))
                return "INTEGER";
            else throw new NotImplementedException(type.Name);
        }

        private static void InitializeDatabase(SqliteConnection c)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode=WAL;" +
                $"PRAGMA busy_timeout={BusyTimeoutMs};";
            cmd.ExecuteNonQuery();
        }

        private string Quote(string id) => '"' + id.Replace("\"", "\"\"") + '"';
    }
}
