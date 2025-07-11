using Dapper;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.Engine.Pagers;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using SQLitePCL;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.Xml;


namespace Grayjay.ClientServer.Database
{
    public class DatabaseCollection<I, T> where I : DBIndex<T>
    {
        private string _table;
        private DatabaseConnection _connection;

        private string[] _indexProperties;
        private string[] _indexOnlyProperties;
        private List<(string, Ordering)> _indexOrderingProperties;

        private string _indexPropertiesQuery;
        private string _indexOnlyPropertiesQuery;
        private string _orderingQuery;
        private string _select;
        private string _selectIndexOnly;

        public DatabaseCollection(DatabaseConnection connection, string table)
        {
            _table = table;
            _connection = connection;

            List<PropertyInfo> props = typeof(I).GetProperties()
                .Where(x => x.GetCustomAttribute<IgnoreAttribute>() == null)
                .ToList();

            //TODO: Investigate AOT
            _indexProperties = props
                .Select(x => x.Name)
                .ToArray();
            _indexOnlyProperties = _indexProperties
                .Where(x => x != nameof(DBIndex<I>.Serialized))
                .ToArray();
            //TODO: Investigate AOT
            _indexOrderingProperties = props
                .Select(x => (x, x.GetCustomAttribute<OrderAttribute>()))
                .Where(x => x.Item2 != null)
                .OrderBy(y => y.Item2.Priority)
                .Select(x => (x.x.Name, x.Item2.Ordering))
                .ToList();
            _indexPropertiesQuery = string.Join(", ", _indexProperties);
            _indexOnlyPropertiesQuery = string.Join(", ", _indexOnlyProperties);
            _select = $"SELECT {_indexPropertiesQuery} FROM {table}";
            _selectIndexOnly = $"SELECT {_indexOnlyPropertiesQuery} FROM {table}";
            _orderingQuery = (_indexOrderingProperties.Any()) ? $" ORDER BY {string.Join(", ", 
                _indexOrderingProperties.Select(x => $"{x.Item1} {((x.Item2 == Ordering.Descending) ? "DESC" : "ASC")}"))}" : 
                "";
        }


        public I Get(long id)
        {
            return _connection.SQL((x) => x
                .QueryFirst<I>($"{_select} WHERE ID = @Id", new
                {
                    Id = id
                }));
        }
        public List<I> GetAll()
        {
            return _connection.SQL((x) => x
                .Query<I>(_select)
                .ToList());
        }
        public List<I> GetAllIndexes()
        {
            return _connection.SQL((x) => x
                .Query<I>(_selectIndexOnly, new { })
                .ToList());
        }

        public I GetIndex(long id)
        {
            return _connection.SQL((x) => x
                .QueryFirst<I>($"{_selectIndexOnly} WHERE ID = @Id", new
                {
                    Id = id
                }));
        }

        public long Count()
        {
            return (long)_connection.SQL((x) => x.ExecuteScalar($"SELECT COUNT(*) FROM {_table}"));
        }

        public long Create(I obj)
        {
            //TODO: Generalize "ID"
            var insertProps = _indexProperties.Where(x => x != nameof(DBIndex<T>.ID));
            var properties = string.Join(", ", insertProps);
            var setters = string.Join(", ", insertProps.Select(x => $"@{x}"));
            return (long)_connection.SQL((x) => x.ExecuteScalar($"INSERT INTO {_table} ({properties}) VALUES ({setters}); SELECT last_insert_rowid();", obj));
        }
        public void Update(I obj)
        {
            var setters = string.Join(", ", _indexProperties.Select(x => $"{x} = @{x}"));
            _connection.SQL((x) => x.Execute($"UPDATE {_table} SET {setters} WHERE ID = @ID", obj));
        }

        //TODO: Fix this, conflicts dont work
        /*
        public long UpdateOrInsert(I obj)
        {
            var dataProps = _indexProperties.Where(p => p != nameof(DBIndex<T>.ID)).ToArray();
            var columns = string.Join(", ", dataProps);
            var values = string.Join(", ", dataProps.Select(p => $"@{p}"));
            var setters = string.Join(", ", dataProps.Select(p => $"{p}=excluded.{p}"));
            return (long)_connection.SQL(c => c.ExecuteScalar($@"INSERT INTO {_table} ({columns}) VALUES ({values}) ON CONFLICT(ID) DO UPDATE SET {setters}; SELECT last_insert_rowid();", obj));
        }
        */

        public void Delete(long id)
        {
            _connection.SQL((x) => x.Execute($"DELETE FROM {_table} WHERE ID = @ID", new { ID = id }));
        }
        public void DeleteAll()
        {
            _connection.SQL((x) => x.Execute($"DELETE FROM {_table}"));
        }


        //Queries
        public I[] Query(string propertyName, object value)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} = @Val", new
            {
                Val = value
            }).ToArray());
        }
        public I[] QueryLike(string propertyName, object value)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} LIKE @Val", new
            {
                Val = value
            }).ToArray());
        }
        public I[] QueryIn(string propertyName, Array values)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} IN @Vals", new
            {
                Vals = values
            }).ToArray());
        }
        public I[] QueryGreater(string propertyName, object value)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} > @Val", new
            {
                Val = value
            }).ToArray());
        }
        public I[] QuerySmaller(string propertyName, object value)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} < @Val", new
            {
                Val = value
            }).ToArray());
        }
        public I[] QueryBetween(string propertyName, object lower, object upper)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} > @ValLower AND {propertyName} < @ValUpper", new
            {
                ValLower = lower,
                ValUpper = upper
            }).ToArray());
        }

        //Pages
        public I[] Page(int page, int pageSize)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} {_orderingQuery} LIMIT @PageSize OFFSET @Offset", new
            {
                PageSize = pageSize,
                Offset = page * pageSize
            }).ToArray());
        }
        public IPager<I> Pager(int pageSize)
            => new AdhocPager<I>(x => Page(x - 1, pageSize));
        public IPager<X> Pager<X>(int pageSize, Func<I, X> converter)
            => new AdhocPager<X>(x => Page(x - 1, pageSize).Select(converter).ToArray());

        public I[] QueryPage(string propertyName, object value, int page, int pageSize)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} = @Val {_orderingQuery} LIMIT @PageSize OFFSET @Offset", new
            {
                Val = value,
                PageSize = pageSize,
                Offset = page * pageSize
            }).ToArray());
        }
        public IPager<I> QueryPager(string propertyName, object value, int pageSize)
            => new AdhocPager<I>(x => QueryPage(propertyName, value, x - 1, pageSize));
        public IPager<X> QueryPager<X>(string propertyName, object value, int pageSize, Func<I, X> converter)
            => new AdhocPager<X>(x => QueryPage(propertyName, value, x - 1, pageSize).Select(converter).ToArray());

        public I[] QueryLikePage(string propertyName, object value, int page, int pageSize)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} LIKE @Val {_orderingQuery} LIMIT @PageSize OFFSET @Offset", new
            {
                Val = value,
                PageSize = pageSize,
                Offset = page * pageSize
            }).ToArray());
        }
        public IPager<I> QueryLikePager(string propertyName, object value, int pageSize)
            => new AdhocPager<I>(x => QueryLikePage(propertyName, value, x - 1, pageSize));
        public IPager<X> QueryLikePager<X>(string propertyName, object value, int pageSize, Func<I, X> converter)
            => new AdhocPager<X>(x => QueryLikePage(propertyName, value, x - 1, pageSize).Select(converter).ToArray());

        public I[] QueryInPage(string propertyName, IEnumerable values, int page, int pageSize)
        {
            return _connection.SQL((x) => x.Query<I>($"{_select} WHERE {propertyName} IN @Vals {_orderingQuery} LIMIT @PageSize OFFSET @Offset", new
            {
                Vals = values,
                PageSize = pageSize,
                Offset = page * pageSize
            }).ToArray());
        }
        public IPager<I> QueryInPager(string propertyName, IEnumerable value, int pageSize)
            => new AdhocPager<I>(x => QueryInPage(propertyName, value, x - 1, pageSize));
        public IPager<X> QueryInPager<X>(string propertyName, IEnumerable value, int pageSize, Func<I, X> converter)
            => new AdhocPager<X>(x => QueryInPage(propertyName, value, x - 1, pageSize).Select(converter).ToArray());



    }
}
