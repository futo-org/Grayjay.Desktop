using Grayjay.ClientServer.States;
using System.Collections.Concurrent;
using System.Text;
using System.Xml.Linq;

namespace Grayjay.ClientServer.Store
{
    public abstract class RawUniqueStore<T> where T: RawUniqueStore<T>
    {
        public string Name { get; set; }

        public DirectoryInfo Directory { get; private set; }
        public bool InMemory = false;

        private ConcurrentDictionary<string, byte[]> _data = new ConcurrentDictionary<string, byte[]>();

        public RawUniqueStore(string name, DirectoryInfo parentDir = null)
        {
            if (parentDir == null)
                parentDir = StateApp.GetAppDirectory();

            Name = name;
            Directory = new DirectoryInfo(Path.Combine(parentDir.FullName, name));
            if (!Directory.Exists)
                Directory.Create();
        }

        public T WithMemory()
        {
            InMemory = true;
            return (T)this;
        }

        public T Load()
        {
            if(InMemory)
                foreach(var file in Directory.GetFiles())
                    _data[file.Name] = File.ReadAllBytes(file.FullName);
            return (T)this;
        }

        public void Delete(string id)
        {
            byte[] removed = null;
            File.Delete(Path.Combine(Directory.FullName, id));
            if (InMemory)
                _data.Remove(id, out removed);
        }

        public void Write(string id, byte[] data)
        {
            File.WriteAllBytes(Path.Combine(Directory.FullName, id), data);
            if(InMemory)
                _data[id] = data;
        }
        public byte[] Read(string id)
        {
            if (InMemory)
            {
                if (!_data.ContainsKey(id))
                    throw new InvalidOperationException($"Plugin script for [{id}] does not exist");
                return _data[id];
            }
            return File.ReadAllBytes(Path.Combine(Directory.FullName, id));
        }
    }

    public class StringUniqueStore : RawUniqueStore<StringUniqueStore>
    {
        public StringUniqueStore(string name, DirectoryInfo parentDir = null) : base(name, parentDir)
        {
        }
        
        public void Write(string id, string data)
        {
            Write(id, Encoding.UTF8.GetBytes(data));
        }
        public string Read(string id)
        {
            return Encoding.UTF8.GetString(base.Read(id));
        }
    }

}
