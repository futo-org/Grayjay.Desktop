using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Grayjay.ClientServer.Settings;
using Grayjay.Engine.Exceptions;
using Grayjay.Desktop.POC;

namespace Futo.PlatformPlayer.States
{
    public class StateDeveloper
    {
        public string CurrentDevID { get; private set; }
        private int _devLogsIndex = 0;
        private readonly List<DevLog> _devLogs = new List<DevLog>();
        private readonly List<DevHttpExchange> _devHttpExchanges = new List<DevHttpExchange>();

        public DevProxySettings DevProxy { get; set; }

        public string TestState { get; set; }

        public bool IsPlaybackTesting => GrayjayDevSettings.Instance.DeveloperMode && TestState == "TestPlayback";

        public void InitializeDev(string id)
        {
            CurrentDevID = id;
            lock (_devLogs)
            {
                _devLogs.Clear();
            }
        }

        public T HandleDevCall<T>(string devId, string contextName, bool printResult, Func<T> handle)
        {
            T resp = default;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                resp = handle();
            }
            catch (InvalidCastException castEx)
            {
                Logger.e("StateDeveloper", $"Wrapped Exception: {castEx.Message}", castEx);
                var exMsg = $"Call [{contextName}] returned incorrect type. Expected [{typeof(T).Name}].\nCastException: {castEx.Message}";
                LogDevException(devId, exMsg);
                throw castEx;
            }
            catch (ScriptException ex)
            {
                Logger.e("StateDeveloper", $"Wrapped Exception: {ex.Message}", ex);
                LogDevException(devId, $"Call [{contextName}] failed due to: ({ex.GetType().Name}) {ex.Message}" + (ex.StackTrace != null ? $"\n{ex.StackTrace}" : ""));
                throw ex;
            }
            catch (Exception ex)
            {
                Logger.e("StateDeveloper", $"Wrapped Exception: {ex.Message}", ex);
                LogDevException(devId, $"Call [{contextName}] failed due to: ({ex.GetType().Name}) {ex.Message}");
                throw ex;
            }

            stopwatch.Stop();
            var printValue = string.Empty;
            if (printResult)
            {
                if (resp is bool)
                    printValue = resp.ToString();
                else if (resp is IList<object> list)
                    printValue = list.Count.ToString();
            }

            LogDevInfo(devId, $"Call [{contextName}] successful [{stopwatch.ElapsedMilliseconds}ms] {printValue}");
            return resp;
        }

        public void LogDevException(string devId, string msg)
        {
            if (CurrentDevID == devId)
            {
                lock (_devLogs)
                {
                    _devLogsIndex++;
                    _devLogs.Add(new DevLog(_devLogsIndex, devId, "EXCEPTION", msg));
                }
            }
        }

        public void LogDevInfo(string devId, string msg)
        {
            if (CurrentDevID == devId)
            {
                lock (_devLogs)
                {
                    _devLogsIndex++;
                    _devLogs.Add(new DevLog(_devLogsIndex, devId, "INFO", msg));
                }
            }
        }

        public List<DevLog> GetLogs(int startIndex)
        {
            lock (_devLogs)
            {
                var index = _devLogs.FindIndex(log => log.Id == startIndex);
                return _devLogs.Skip(index + 1).ToList();
            }
        }

        public void AddDevHttpExchange(DevHttpExchange exchange)
        {
            lock (_devHttpExchanges)
            {
                if (_devHttpExchanges.Count > 15)
                    _devHttpExchanges.RemoveAt(0);
                _devHttpExchanges.Add(exchange);
            }
        }

        public List<DevHttpExchange> GetHttpExchangesAndClear()
        {
            lock (_devHttpExchanges)
            {
                var data = new List<DevHttpExchange>(_devHttpExchanges);
                _devHttpExchanges.Clear();
                return data;
            }
        }

        public void SetDevClientSettings(Dictionary<string, string> settings)
        {
            var client = StatePlatform.GetDevClient();
            client?.ReplaceDescriptorSettings(settings);
        }

        private IPager<PlatformContent> _homePager;
        private int _pagerIndex = 0;


        public static readonly string DEV_ID = "DEV";

        private static StateDeveloper _instance;
        public static StateDeveloper Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new StateDeveloper();
                return _instance;
            }
        }


        [Serializable]
        public class DevLog
        {
            public int Id { get; }
            public string DevId { get; }
            public string Type { get; }
            public string Log { get; }

            public DevLog(int id, string devId, string type, string log)
            {
                Id = id;
                DevId = devId;
                Type = type;
                Log = log;
            }
        }

        [Serializable]
        public class DevHttpRequest
        {
            public string Method { get; }
            public string Url { get; }
            public Dictionary<string, string> Headers { get; }
            public string Body { get; }
            public int Status { get; }

            public DevHttpRequest(string method, string url, Dictionary<string, string> headers, string body, int status = 0)
            {
                Method = method;
                Url = url;
                Headers = headers;
                Body = body;
                Status = status;
            }
        }

        [Serializable]
        public class DevHttpExchange
        {
            public DevHttpRequest Request { get; }
            public DevHttpRequest Response { get; }

            public DevHttpExchange(DevHttpRequest request, DevHttpRequest response)
            {
                Request = request;
                Response = response;
            }
        }

        [Serializable]
        public class DevProxySettings
        {
            public string Url { get; }
            public int Port { get; }

            public DevProxySettings(string url, int port)
            {
                Url = url;
                Port = port;
            }
        }
    }
}