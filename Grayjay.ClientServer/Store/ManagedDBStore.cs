using Grayjay.ClientServer.Database;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.States;
using Grayjay.Engine.Pagers;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection.Metadata.Ecma335;

namespace Grayjay.ClientServer.Store
{
    public class ManagedDBStore<I, T>: IDisposable where I : DBIndex<T>, new()
    {
        private DatabaseConnection _connection;
        private DatabaseCollection<I, T> _collection;

        private List<IndexDescriptor<I>> _indexes = new List<IndexDescriptor<I>>();

        private (Func<I, object>, ConcurrentDictionary<object, I>)? _withUnique;


        public string Name { get; private set; }

        public ManagedDBStore(DatabaseConnection connection, string name)
        {
            Name = name;
            _connection = connection;
            _collection = new DatabaseCollection<I, T>(connection, name);
        }
        public ManagedDBStore(string name)
        {
            var connection = StateApp.Connection;
            Name = name;
            _connection = connection;
            _collection = new DatabaseCollection<I, T>(connection, name);
        }

        //Building
        public ManagedDBStore<I, T> WithIndex(Func<I, object> keySelector, ConcurrentDictionary<object, I> index, bool allowChange, bool withUnique = false)
        {
            _indexes.Add(new IndexDescriptor<I>(keySelector, index, allowChange));

            if(withUnique)
                WithUnique(keySelector, index);
            return this;
        }
        public ManagedDBStore<I, T> WithUnique(Func<I, object> keySelector, ConcurrentDictionary<object, I> index)
        {
            if (_withUnique != null)
                throw new InvalidOperationException("Only 1 unique allowed");
            WithIndex(keySelector, index, true);
            _withUnique = (keySelector, index);
            return this;
        }
        public ManagedDBStore<I, T> Load()
        {

            if(_indexes.Any())
            {
                var allItems = GetAllIndexes();
                foreach(var index in _indexes)
                {
                    foreach (var item in allItems)
                        index.Collection[index.KeySelector(item)] = item;
                }
            }

            return this;
        }

        //Functionality
        public I GetUnique(I obj)
        {
            if (_withUnique == null)
                throw new InvalidOperationException($"Unique is not configured for {Name}");
            var key = _withUnique.Value.Item1(obj);
            if (key == null || !_withUnique.Value.Item2.ContainsKey(key))
                return null;
            return _withUnique.Value.Item2[key];
        }
        public bool IsUnique(I obj)
        {
            if (_withUnique == null)
                throw new InvalidOperationException($"Unique is not configured for {Name}");
            var key = _withUnique.Value.Item1(obj);
            return _withUnique.Value.Item2.ContainsKey(key);
        }



        public I GetIndex(long id)
        {
            return _collection.GetIndex(id);
        }
        public I Get(long id)
        {
            return _collection.Get(id);
        }
        public List<I> GetAllIndexes()
        {
            return _collection.GetAllIndexes();
        }

        public List<T> GetAllObjects()
        {
            return _collection.GetAll().Select(x => x.Object).ToList();
        }
        public List<I> GetAll(bool deserialize = false)
        {
            var results = _collection.GetAll();
            foreach (var result in results)
                result.EnsureDeserialized();
            return results;
        }

        public long Count()
        {
            return _collection.Count();
        }

        public I Insert(T obj)
        {
            var newIndex = new I();
            newIndex.FromObject(obj);

            if(_withUnique != null)
            {
                var unique = GetUnique(newIndex);
                if (unique != null)
                    return unique;
            }

            newIndex.ID = _collection.Create(newIndex);
            newIndex.Serialized = null;

            if(_indexes.Any())
            {
                foreach(var index in _indexes)
                {
                    var key = index.KeySelector(newIndex);
                    index.Collection[key] = newIndex;
                }
            }
            return newIndex;
        }
        public void Update(long id, T obj)
        {
            var existing = (_indexes.Any(x => x.CheckChange)) ? _collection.Get(id) : null;

            var newIndex = new I();
            newIndex.FromObject(obj);
            newIndex.ID = id;
            _collection.Update(newIndex);
            newIndex.Serialized = null;

            if(_indexes.Any())
            {
                foreach (var index in _indexes)
                {
                    var key = index.KeySelector(newIndex);
                    if(index.CheckChange && existing != null)
                    {
                        var keyExisting = index.KeySelector(existing);
                        if (keyExisting != null)
                            index.Collection.Remove(keyExisting, out _);
                    }
                    index.Collection[key] = newIndex;
                }
            }
        }

        /*
        public void UpdateOrInsert(long id, T obj)
        {
            var existing = (_indexes.Any(x => x.CheckChange)) ? _collection.Get(id) : null;

            var newIndex = new I();
            newIndex.FromObject(obj);
            newIndex.ID = id;
            _collection.UpdateOrInsert(newIndex);
            newIndex.Serialized = null;

            if (_indexes.Any())
            {
                foreach (var index in _indexes)
                {
                    var key = index.KeySelector(newIndex);
                    if (index.CheckChange && existing != null)
                    {
                        var keyExisting = index.KeySelector(existing);
                        if (keyExisting != null)
                            index.Collection.Remove(keyExisting, out _);
                    }
                    index.Collection[key] = newIndex;
                }
            }
        }
        */

        public void Delete(I indexObj)
        {
            _collection.Delete(indexObj.ID);
            foreach(var index in _indexes)
                index.Collection.Remove(index.KeySelector(indexObj), out _);
        }
        public void Delete(long id)
        {
            _collection.Delete(id);
            foreach(var index in _indexes)
            {
                var toDelete = index.Collection.Where(x => x.Value.ID == id).Select(y => y.Key);
                foreach (var del in toDelete)
                    index.Collection.Remove(del, out _);
            }
        }
        public void DeleteAll()
        {
            _collection.DeleteAll();
            foreach (var index in _indexes)
                index.Collection.Clear();
        }

        public I[] Query(string key, object value) => _collection.Query(key, value);
        public I[] QueryGreater(string key, object lower) => _collection.QueryGreater(key, lower);
        public I[] QuerySmaller(string key, object upper) => _collection.QuerySmaller(key, upper);
        public I[] QueryLike(string key, object like) => _collection.QueryLike(key, like);
        public I[] QueryIn(string key, Array values) => _collection.QueryIn(key, values);

        public IPager<I> Pager(int pageSize) => _collection.Pager(pageSize);
        public IPager<X> Pager<X>(int pageSize, Func<I, X> modifier) => _collection.Pager(pageSize, modifier);

        public IPager<I> QueryPager(string key, object value, int pageSize) => _collection.QueryPager(key, value, pageSize);
        public IPager<X> QueryPager<X>(string key, object value, int pageSize, Func<I, X> modifier) => _collection.QueryPager(key, value, pageSize, modifier);
        public IPager<I> QueryLikePager(string key, object value, int pageSize) => _collection.QueryLikePager(key, value, pageSize);
        public IPager<X> QueryLikePager<X>(string key, object value, int pageSize, Func<I, X> modifier) => _collection.QueryLikePager(key, value, pageSize, modifier);
        public IPager<I> QueryInPager(string key, IEnumerable values, int pageSize) => _collection.QueryInPager(key, values, pageSize);
        public IPager<X> QueryInPager<X>(string key, IEnumerable values, int pageSize, Func<I, X> modifier) => _collection.QueryInPager(key, values, pageSize, modifier);

        public void Dispose()
        {
            _connection = null;
        }

        public class IndexDescriptor<I>
        {
            public Func<I, object> KeySelector { get; private set; }
            public ConcurrentDictionary<object, I> Collection { get; private set; }
            public bool CheckChange { get; set; }

            public IndexDescriptor(Func<I, object> keySelector, ConcurrentDictionary<object, I> collection, bool checkChange)
            {
                KeySelector = keySelector;
                Collection = collection;
                CheckChange = checkChange;
            }
        }
    }
}
