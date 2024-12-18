using Grayjay.ClientServer.Crypto;
using Grayjay.ClientServer.Serializers;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using static Grayjay.ClientServer.States.StateBackup;

namespace Grayjay.ClientServer.Store
{
    public interface IManagedStore
    {
        string Name { get; }


        Task<ReconstructionResult> ImportReconstructions(List<string> items, ImportCache cache = null, Action<int, int> onProgress = null);
        List<string> GetAllReconstructionStrings(bool withHeader = false);
    }
    public class ReconstructionResult
    {
        public int Success { get; set; }
        public List<Exception> Exceptions { get; set; }
        public List<string> Messages { get; set; }
    }

    public class ManagedStore<T>: IManagedStore
    {
        public string Name { get; set; }

        public DirectoryInfo Directory { get; private set; }

        private Func<T, object> _withUnique;
        private bool _withBackup;
        private bool _withEncryption;

        private bool _isLoaded;


        private Dictionary<T, StoreFile> _files = new Dictionary<T, StoreFile>();

        private ReconstructStore<T>? _reconstructStore = null;


        public ManagedStore(string name, DirectoryInfo parentDir = null)
        {
            if (parentDir == null)
                parentDir = StateApp.GetAppDirectory();

            Name = name;
            Directory = new DirectoryInfo(Path.Combine(parentDir.FullName, name));
            if (!Directory.Exists)
                Directory.Create();
        }

        public ManagedStore<T> WithUnique(Func<T, object> withUnique)
        {
            _withUnique = withUnique;
            return this;
        }
        public ManagedStore<T> WithBackup()
        {
            _withBackup = true;
            return this;
        }
        public ManagedStore<T> WithRestore<R>() where R: ReconstructStore<T>, new ()
        {
            _reconstructStore = new R();
            return this;
        }
        public ManagedStore<T> WithRestore(ReconstructStore<T> recon)
        {
            _reconstructStore = recon;
            return this;
        }

        public ManagedStore<T> WithEncryption()
        {
            _withEncryption = true;
            return this;
        }

        public ManagedStore<T> Load()
        {
            lock (_files)
            {
                _files.Clear();
                var newObjs = Directory
                    .GetFiles()
                    .Select(x => Path.GetFileNameWithoutExtension(x.Name))
                    .Distinct()
                    .Select((fileId) =>
                    {
                        var mfile = new StoreFile(fileId, Directory.FullName, _withEncryption);
                        var obj = mfile.Load(this, _withBackup);
                        if (obj == null)
                        {
                            Logger.w<ManagedStore<T>>($"Deleting {LogName(mfile.ID)}");
                            mfile.Delete();
                            //TODO: Reconstruction
                        }
                        return (obj, mfile);
                    }).Where(x => x.obj != null);

                if (newObjs != null)
                {
                    foreach (var obj in newObjs)
                    {
                        _files[obj.obj] = obj.mfile;
                    }
                }
            }
            _isLoaded = true;
            return this;
        }


        public int Count()
        {
            return _files.Count;
        }
        public List<T> GetObjects()
        {
            lock(_files)
            {
                return _files.Keys.ToList();
            }
        }

        public bool HasObject(Func<T, bool> query)
        {
            lock(_files)
            {
                return _files.Keys.Any(query);
            }
        }
        public T? FindObject(Func<T, bool> query)
        {
            lock(_files)
            {
                return _files.Keys.FirstOrDefault(query);
            }
        }

        public List<T> FindObjects(Func<T, bool> query)
        {
            lock(_files)
            {
                return _files.Keys.Where(query).ToList();
            }
        }

        public T? MaxByNotNull<TKey>(Func<T, TKey?> keySelector)
        {
            lock(_files)
            {
                return _files.Keys.Where(v => keySelector(v) != null).MaxBy(keySelector);
            }
        }

        public T? MaxBy<TKey>(Func<T, TKey> keySelector)
        {
            lock(_files)
            {
                return _files.Keys.MaxBy(keySelector);
            }
        }

        public T? MinByNotNull<TKey>(Func<T, TKey?> keySelector)
        {
            lock(_files)
            {
                return _files.Keys.Where(v => keySelector(v) != null).MinBy(keySelector);
            }
        }

        public T? MinBy<TKey>(Func<T, TKey> keySelector)
        {
            lock(_files)
            {
                return _files.Keys.MinBy(keySelector);
            }
        }

        public void Create<TKey>(Func<T, TKey> keySelector, TKey key, Func<T> create) where TKey : notnull
        {
            lock (_files)
            {
                var entry = _files.Keys.FirstOrDefault(k => keySelector(k).Equals(key));
                if (entry != null)
                    throw new Exception("Key already exists.");

                var created = create();
                Save(created);
            }
        }

        public T Update<TKey>(Func<T, TKey> keySelector, TKey key, Action<T> update) where TKey : notnull
        {
            lock (_files)
            {
                var entry = _files.Keys.FirstOrDefault(k => keySelector(k).Equals(key));
                if (entry == null)
                    throw new Exception("Key not found.");

                update(entry);
                Save(entry);

                return entry;
            }
        }

        public void CreateOrUpdate<TKey>(Func<T, TKey> keySelector, TKey key, Func<T> create, Action<T> update) where TKey : notnull
        {
            lock (_files)
            {
                var entry = _files.Keys.FirstOrDefault(k => keySelector(k).Equals(key));
                if (entry != null)
                {
                    update(entry);
                    Save(entry);
                }
                else
                {
                    var created = create();
                    Save(created);
                }
            }
        }

        public StoreFile? GetFile(T item)
        {
            lock(_files)
            {
                if (_files.ContainsKey(item))
                    return _files[item];
                return null;
            }
        }

        public Task SaveAsync(T item, bool withReconstruction = false, bool onlyExisting = false) => Task.Run(() => Save(item, withReconstruction, onlyExisting));
        public void Save(T item, bool withReconstruction = false, bool onlyExisting = false)
        {
            lock(_files)
            {
                var uniqueVal = (_withUnique != null) ? _withUnique(item) : null;

                var file = GetFile(item);
                if(file != null)
                {
                    var encoded = GJsonSerializer.Serialize(item);
                    file.Write(Encoding.UTF8.GetBytes(encoded), _withBackup);
                    //TODO: reconstruction
                }
                else if(!onlyExisting && (uniqueVal == null || !_files.Any(x=>_withUnique(x.Key).Equals(uniqueVal))))
                {
                    file = SaveNew(item);
                }
            }
        }
        public void SaveAll(List<T> items, bool withReconstruction = false, bool onlyExisting = false)
        {
            foreach (var obj in items)
                Save(obj, withReconstruction, onlyExisting);
        }


        public T? DeleteBy<TKey>(Func<T, TKey> keySelector, TKey key) where TKey : notnull
        {
            lock(_files)
            {
                var entry = _files.Keys.FirstOrDefault(k => keySelector(k).Equals(key));
                if (entry == null)
                    return default(T);

                var file = _files[entry];
                if (file != null)
                {
                    _files.Remove(entry);
                    file.Delete();
                }
                return entry;
            }
        }

        public bool Delete(T obj)
        {
            bool didDelete = false;
            if (obj == null)
                return false;
            lock(_files)
            {
                var file = _files[obj];
                if(file != null)
                {
                    _files.Remove(obj);
                    file.Delete();
                    return true;
                }
            }
            return false;
        }

        public void DeleteAll()
        {
            lock(_files)
            {
                var toDelete = _files.Keys.ToList();
                foreach(var toDel in toDelete)
                {
                    Delete(toDel);
                }
            }
        }

        public void ReplaceAll(List<T> items, bool withReconstruction = false, bool onlyExisting = false)
        {
            lock (_files)
            {
                DeleteAll();
                SaveAll(items, withReconstruction, onlyExisting);
            }
        }

        public StoreFile SaveNew(T obj)
        {
            lock(_files)
            {
                string id = Guid.NewGuid().ToString();
                Logger.i(nameof(ManagedStore<T>), $"New file {LogName(id)}");
                string encoded = GJsonSerializer.Serialize(obj);

                var mfile = new StoreFile(id, Directory.FullName, _withEncryption);
                mfile.Write(Encoding.UTF8.GetBytes(encoded), _withBackup);

                _files[obj] = mfile;
                return mfile;
            }
        }

        private string LogName(string id)
        {
            return $"{Name}: [{typeof(T).Name}] {id}";
        }

        public List<string> GetAllReconstructionStrings(bool withHeader = false)
        {
            if (_reconstructStore == null)
                throw new InvalidOperationException("Can't reconstruct as no reconstrcion is implemented for this type");

            return GetObjects().Select(x => GetReconstructionString(x, withHeader)).ToList();
        }
        public string GetReconstructionString(T obj, bool withHeader)
        {
            if(_reconstructStore == null)
                throw new InvalidOperationException("Can't reconstruct as no reconstrcion is implemented for this type");

            if (withHeader)
                return _reconstructStore.ToReconstructionWithHeader(obj, typeof(T).Name);
            else
                return _reconstructStore.ToReconstruction(obj);
        }

        public async Task<ReconstructionResult> ImportReconstructions(List<string> items, ImportCache cache = null, Action<int, int> onProgress = null)
        {
            int successes = 0;
            List<Exception> exs = new List<Exception>();

            int total = items.Count;
            int finished = 0;

            var builder = new ReconstructStore<T>.Builder();

            foreach (var recon in items)
            {
                onProgress?.Invoke(0, total);
                for(int i = 0; i < 2; i++)
                {
                    try
                    {
                        Logger.i(nameof(ManagedStore<T>), $"Importing {LogName(recon)}");
                        var reconId = await CreateFromReconstruction(recon, builder, cache);
                        successes++;
                        Logger.i(nameof(StateBackup), $"Imported {LogName(reconId)}");
                        break;
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(ManagedStore<T>), "Failed to reconstruct import", ex);
                        if (i == 1)
                            exs.Add(ex);
                    }
                }
            }
            return new ReconstructionResult()
            {
                Success = successes,
                Exceptions = exs,
                Messages = builder.Messages
            };
        }
        public async Task<string> CreateFromReconstruction(string reconstruction, ReconstructStore<T>.Builder builder, ImportCache cache = null, bool withSave = true)
        {
            if (_reconstructStore == null)
                throw new InvalidOperationException("Can't reconstruct as no reconstruction is implemented for this type");

            var id = Guid.NewGuid().ToString();
            var reconstruct = await _reconstructStore.ToObjectWithHeader(id, reconstruction, builder, cache);
            Save(reconstruct);
            return id;
        }
        public async Task<T> FromReconstruction(string reconstruction, ImportCache cache = null)
        {
            if (_reconstructStore == null)
                throw new InvalidOperationException("Can't reconstruct as no reconstruction is implemented for this type");

            var id = Guid.NewGuid().ToString();
            return await _reconstructStore.ToObjectWithHeader(id, reconstruction, new ReconstructStore<T>.Builder(), cache);
        }

        public class StoreFile
        {
            private static byte[] MAGIC_BYTES_ENCRYPTED = new byte[] { (byte)'E', (byte)'N', (byte)'C', (byte)':' };

            public string ID { get; set; }
            public string Directory { get; set; }

            public string FilePath { get; set; }
            public string BackupFilePath { get; set; }
            public string ReconstructionFilePath { get; set; }

            public bool UseEncryption { get; set; }

            public StoreFile(string id, string directory, bool useEncryption = false)
            {
                ID = id;
                Directory = directory;
                FilePath = Path.Combine(directory, id);
                BackupFilePath = Path.Combine(directory, id + ".bak");
                UseEncryption = useEncryption;
            }

            public T Load(ManagedStore<T> store, bool withBackup = true)
            {
                lock (this)
                {
                    try
                    {
                        if (!File.Exists(FilePath))
                            throw new FileNotFoundException();
                        var data = Read();
                        return GJsonSerializer.Deserialize<T>(data);
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(ManagedStore<T>), $"[{store.Name}] Exception: {ex.Message}", ex);
                        if (!(ex is FileNotFoundException))
                            Logger.w(nameof(ManagedStore<T>), $"Failed to parse {store.LogName(ID)}");

                        if(withBackup)
                        {
                            var backupData = ReadBackup();
                            try
                            {
                                if (backupData != null)
                                {
                                    Logger.i<ManagedStore<T>>($"Loading from backup {store.LogName(ID)}");
                                    return GJsonSerializer.Deserialize<T>(backupData);
                                }
                                else Logger.i<ManagedStore<T>>($"No backup exists for {store.LogName(ID)}");
                            }
                            catch(Exception backEx)
                            {
                                Logger.w<ManagedStore<T>>($"Failed to bakfile parse {store.LogName(ID)}", backEx);
                            }
                        }
                    }
                    Logger.w<ManagedStore<T>>($"No object from {store.LogName(ID)}");
                    return default(T);
                }
            }

            public bool IsEncrypted(byte[] bytes)
            {
                if (bytes.Length < MAGIC_BYTES_ENCRYPTED.Length)
                    return false;
                for (int i = 0; i < MAGIC_BYTES_ENCRYPTED.Length; i++)
                    if (bytes[i] != MAGIC_BYTES_ENCRYPTED[i])
                        return false;
                return true;
            }
            public byte[] Encrypt(byte[] bytes)
            {
                return MAGIC_BYTES_ENCRYPTED.Concat(EncryptionProvider.Instance.Encrypt(bytes)).ToArray();
            }
            public byte[] Decrypt(byte[] bytes)
            {
                return EncryptionProvider.Instance.Decrypt(new Span<byte>(bytes, MAGIC_BYTES_ENCRYPTED.Length, bytes.Length - MAGIC_BYTES_ENCRYPTED.Length).ToArray());
            }

            public byte[] Read()
            {
                var bytes = File.ReadAllBytes(FilePath);
                if (IsEncrypted(bytes))
                    return Decrypt(bytes);
                return bytes;
            }
            public byte[] ReadBackup()
            {
                var bytes = File.ReadAllBytes(BackupFilePath);
                if (IsEncrypted(bytes))
                    return Decrypt(bytes);
                return bytes;
            }
            public void Write(byte[] data, bool withBackup = true)
            {
                if (withBackup && File.Exists(FilePath))
                    File.Copy(FilePath, BackupFilePath);

                File.WriteAllBytes(FilePath, !UseEncryption ? data : Encrypt(data));
            }

            public void Delete(bool deleteRecon = true)
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                if (File.Exists(BackupFilePath))
                    File.Delete(BackupFilePath);
                if (deleteRecon && File.Exists(ReconstructionFilePath))
                    File.Delete(ReconstructionFilePath);
            }
        }
    }

    public abstract class ReconstructStore<T>
    {
        protected virtual bool BackupOnSave { get; } = false;
        protected virtual bool BackupOnCreate { get; } = true;

        public string IdentifierName { get; private set; }

        public ReconstructStore(string identifierName = null)
        {
            IdentifierName = identifierName;
        }

        public abstract string ToReconstruction(T obj);
        public abstract T ToObject(string id, string backup, Builder builder, ImportCache cache = null);

        public string ToReconstructionWithHeader(T obj, string fallbackName)
        {
            var identifier = IdentifierName ?? fallbackName;
            return $"@/{identifier}\n{ToReconstruction(obj)}";
        }

        public async Task<T> ToObjectWithHeader(string id, string backup, Builder builder, ImportCache importCache = null)
        {
            if (backup.StartsWith("@/") && backup.Contains("\n"))
                return ToObject(id, backup.Substring(backup.IndexOf("\n") + 1), builder, importCache);
            else
                return ToObject(id, backup, builder, importCache);
        }


        public class Builder
        {
            public List<string> Messages { get; set; } = new List<string>();
        }
    }
    public class ReconstructionException : Exception
    {
        public string Name { get; set; }

        public ReconstructionException(string name, string message, Exception innerException): base(message, innerException)
        {
            Name = name;
        }
    }
}
