using Dapper;
using Grayjay.ClientServer.Database.Indexes;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Reflection;
using Grayjay.ClientServer.States;

namespace Grayjay.ClientServer.Database
{
    public class DatabaseConnection : IDisposable
    {
        private string _connectionString;
        public SqliteConnection Connection { get; private set; }

        public DatabaseConnection()
        {
            _connectionString = $"Data Source={Path.Combine(StateApp.GetAppDirectory().FullName, "database.db")}";
            Connection = new SqliteConnection(_connectionString);
            Connection.Open();
        }

        public T SQL<T>(Func<SqliteConnection, T> handle)
        {
            using(var connection = new SqliteConnection(_connectionString))
            {
                return handle(connection);
            }
        }

        public SqliteCommand GetCommand(string sql, params SqliteParameter[] parameters)
        {
            var command = Connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(parameters);
            return command;
        }

        public void EnsureTable<T>(string table, string primaryKey = "ID", bool autoIncrement = true)
        {
            var properties = typeof(T).GetProperties()
                .Where(x => x.GetCustomAttribute<IgnoreAttribute>() == null);
            var propertiesSQL = properties
                .Select(x => (x.Name, GetSQLType(x.PropertyType)))
                .ToArray();
            var result = SQL((x) => x.QueryFirstOrDefault($"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';"));

            if(result != null)
            {
                try
                {
                    var columns = SQL((x) => x.Query($"PRAGMA table_info({table})"));
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
                            else if(column.type != prop.Item2)
                                throw new InvalidDataException($"Incorrect column type {prop.Item2} = {column.type}");
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Deleting table because broken: " + ex.Message);
                    SQL((x) => x.Execute($"DROP TABLE {table}"));
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
                string tableQuery = $"CREATE TABLE {table}\n({string.Join(",\n", definitions)})";
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


        public void Dispose()
        {
            Connection?.Close();
            Connection?.Dispose();
        }
    }
}
