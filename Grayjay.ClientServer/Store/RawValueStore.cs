using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using System.Collections.Concurrent;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Grayjay.ClientServer.Store
{
    public abstract class RawValueStore<T, V> where T: RawValueStore<T, V>
    {
        public string Name { get; set; }
        public V Default { get; private set; }
        public V Value { get; private set; } //TODO: Direct is a bit annoying, requires external locking.
        public DirectoryInfo Directory { get; private set; }

        public string FilePath => Path.Combine(Directory.FullName, Name);


        public RawValueStore(string name, V def = default(V), DirectoryInfo parentDir = null)
        {
            if (parentDir == null)
                parentDir = StateApp.GetAppDirectory();

            Default = def;
            Name = name;
            Directory = new DirectoryInfo(parentDir.FullName);
            if (!Directory.Exists)
                Directory.Create();
        }

        public void Save(V data)
        {
            lock (this)
            {
                var serialized = SerializeValue(data);
                Value = data;
                Write(serialized);
            }
        }
        public void SaveThis()
        {
            lock (this)
            {
                var serialized = SerializeValue(Value);
                Write(serialized);
            }
        }

        public abstract V DeserializeValue(byte[] data);
        public abstract byte[] SerializeValue(V data);


        protected void Write(byte[] data)
        {
            File.WriteAllBytes(FilePath, data);
        }
        protected byte[] Read()
        {
            if (!File.Exists(FilePath))
                return null;
            return File.ReadAllBytes(FilePath);
        }



        public T Load()
        {
            var raw = Read();
            try
            {
                Value = raw != null ? DeserializeValue(raw) : Default;
            }
            catch(Exception ex)
            {
                Logger.e(nameof(RawValueStore<T, V>), "Failed to deserialize value, defaulting", ex);
                Value = Default;
            }
            return (T)this;
        }
    }

    public class StringArrayStore : RawValueStore<StringArrayStore, string[]>
    {
        public StringArrayStore(string name, string[] def = null, DirectoryInfo parentDir = null) : base(name, def, parentDir)
        {
        }

        public string[] GetCopy()
        {
            lock (this)
            {
                return Value.ToArray();
            }
        }
        public int IndexOf(string value)
        {
            lock (this)
            {
                return Array.IndexOf(Value ?? new string[0], value);
            }
        }
        public bool Contains(string value)
        {
            lock(this)
            {
                return Value?.Contains(value) ?? false;
            }
        }
        public void AddSave(string value)
        {
            lock (this)
            {
                Save((Value ?? new string[0]).Concat(new string[] { value }).ToArray());
            }
        }

        public override byte[] SerializeValue(string[] data)
        {
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
        }
        public override string[] DeserializeValue(byte[] data)
        {
            return JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(data));
        }
    }

    public class StringStore : RawValueStore<StringStore, string>
    {
        public StringStore(string name, string def = null, DirectoryInfo parentDir = null) : base(name, def, parentDir)
        {
        }

        public string GetCopy()
        {
            lock (this)
            {
                return Value;
            }
        }

        public override byte[] SerializeValue(string data)
        {
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
        }
        public override string DeserializeValue(byte[] data)
        {
            return JsonSerializer.Deserialize<string>(Encoding.UTF8.GetString(data));
        }
    }

    public class DictionaryStore<K, V> : RawValueStore<DictionaryStore<K, V>, Dictionary<K, V>>
    {
        public DictionaryStore(string name, Dictionary<K, V>? def = null, DirectoryInfo? parentDir = null) : base(name, def ?? new Dictionary<K, V>(), parentDir)
        {
        }

        public void SetValue(K key, V value)
        {
            lock (this)
            {
                Value[key] = value;
            }
        }
        public void SetAllValues(Dictionary<K, V> values, Func<K, V, V, bool> condition = null)
        {
            lock (this)
            {
                foreach (var kv in values)
                {
                    if(condition == null || condition(kv.Key, kv.Value, (values.ContainsKey(kv.Key)) ? values[kv.Key] : default(V)))
                        values[kv.Key] = kv.Value;
                }
            }
        }
        public void SetAllAndSave(Dictionary<K, V> values, Func<K, V, V, bool> condition = null)
        {
            SetAllValues(values, condition);
            SaveThis();
        }
        public void SetAndSave(K key, V value)
        {
            SetValue(key, value);
            SaveThis();
        }
        public V? GetValue(K key, V? def)
        {
            lock (this)
            {
                if (Value.TryGetValue(key, out var v))
                    return v;
            }
            return def;
        }

        public Dictionary<K,V> All()
        {
            lock (this)
            {
                return Value?.ToDictionary() ?? new Dictionary<K, V>();
            }
        }

        public override byte[] SerializeValue(Dictionary<K, V> data)
        {
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
        }
        public override Dictionary<K, V> DeserializeValue(byte[] data)
        {
            return JsonSerializer.Deserialize<Dictionary<K, V>>(Encoding.UTF8.GetString(data));
        }

    }
}
