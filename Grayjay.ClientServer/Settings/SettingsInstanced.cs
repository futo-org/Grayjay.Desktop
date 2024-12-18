using Grayjay.Engine.Setting;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using System.Xml.Linq;
using System.Text.Json;

namespace Grayjay.ClientServer.Settings
{
    public abstract class SettingsInstanced<T> : Settings<T> where T: SettingsInstanced<T>, new ()
    {
        public abstract string FileName { get; }


        private static object _lock = new object();
        private static T _settings = null;
        public static T Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_settings == null)
                    {
                        try
                        {
                            string json = StateApp.ReadTextFile((new T()).FileName);
                            _settings = (!string.IsNullOrEmpty(json)) ? FromText(json) : new T();
                        }
                        catch (Exception ex)
                        {
                            Logger.e(nameof(GrayjaySettings), "Failed to load Grayjay settings", ex);
                            _settings = new T();
                        }

                    }
                    return _settings;
                }
            }
        }

        public void Save()
        {
            lock(_lock)
            {
                string json = ToText((T)this);
                StateApp.WriteTextFile(FileName, json);
            }
        }

        public void Replace()
        {
            lock(_lock)
            {
                Save();
                _settings = (T)this;
            }
        }
    }
}
