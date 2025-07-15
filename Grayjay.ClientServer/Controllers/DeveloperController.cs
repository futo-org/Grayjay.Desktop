using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml;
using Futo.PlatformPlayer.States;
using Grayjay.ClientServer.Developer;
using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Internal;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ClearScript;
using Newtonsoft.Json;
using static Grayjay.Engine.Packages.PackageHttp;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class DeveloperController : ControllerBase
    {
        private static ManagedHttpClient _client = new ManagedHttpClient();
        private static GrayjayPlugin _testPlugin = null;
        private static (string, SourceAuth) _testPluginAuth = (null, null);
        private static GrayjayPlugin TestPluginOrThrow => _testPlugin ?? throw new InvalidOperationException("Attempted to use test plugin without plugin");
        private static Dictionary<string, V8RemoteObject> _testPluginVariables = new Dictionary<string, V8RemoteObject>();

        private static StringStore lastDevUrl = new StringStore("lastDevUrl", null).Load();

        [HttpGet]
        public async Task<IActionResult> LoginTestPlugin()
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                var plugin = TestPluginOrThrow;
                var auth = await StatePlugins.AuthenticatePlugin(plugin.Config);
                _testPluginAuth = (plugin.Config.ID, auth);
                return Ok(true);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.GetType().Name + ":" + ex.Message);
            }
        }
        [HttpGet]
        public IActionResult LogoutTestPlugin()
        {
            if (!IsDeveloperMode())
                return NotFound();
            _testPluginAuth = (null, null);
            return Ok(true);
        }


        [HttpGet]
        public IActionResult IsDeveloper()
        {
            return Ok(IsDeveloperMode());
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (!IsDeveloperMode())
                return NotFound();
            var html = Encoding.UTF8.GetString(ReadResource("Grayjay.ClientServer.Developer.Embed.index.html"));
            html = html
                .Replace("SUPPORT_INTEGRATION: true", "SUPPORT_INTEGRATION: true");
            if (!string.IsNullOrEmpty(lastDevUrl.Value))
                html = html.Replace("LAST_DEV_URLS: []", "LAST_DEV_URLS: [" + JsonConvert.SerializeObject(lastDevUrl.Value) + "]");
            return File(Encoding.UTF8.GetBytes(html), "text/html");
        }
        [HttpGet]
        public IActionResult DevBridge()
        {
            if (!IsDeveloperMode())
                return NotFound();
            return File(ReadResource("Grayjay.ClientServer.Developer.Embed.dev_bridge.js"), "application/javascript");
        }

        [HttpGet]
        public IActionResult Source()
        {
            if (!IsDeveloperMode())
                return NotFound();
            return File(Encoding.UTF8.GetBytes(Grayjay.Engine.Resources.ScriptSource), "application/javascript");
        }



        [HttpGet]
        public IActionResult SourceDocs()
        {
            if (!IsDeveloperMode())
                return NotFound();
            return File(Encoding.UTF8.GetBytes("const sourceDocs = " + System.Text.Json.JsonSerializer.Serialize(GrayjayPlugin.GetJSDocs(), new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })), "application/javascript");
        }

        [HttpGet]
        public IActionResult SourceDocUrls()
        {
            if (!IsDeveloperMode())
                return NotFound();
            return File(Encoding.UTF8.GetBytes("const sourceDocUrls = {}"), "application/javascript");
        }

        [HttpPost]
        public IActionResult GetWarnings([FromBody] PluginConfig req)
        {
            if (!IsDeveloperMode())
                return NotFound();
            return Ok(req.GetWarnings());
        }
        [HttpPost]
        public IActionResult UpdateTestPlugin([FromBody] PluginConfig config)
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                _testPluginVariables.Clear();

                var encryptedAuth = (_testPluginAuth.Item1 == config.ID) ? PluginDescriptor.Encryption.Encrypt(System.Text.Json.JsonSerializer.Serialize(_testPluginAuth.Item2)) : null;

                var client = new PluginHttpClient(null, null, null);
                var clientAuth = new PluginHttpClient(null, (_testPluginAuth.Item1 == config.ID) ? _testPluginAuth.Item2 : null, null);
                _testPlugin = GrayjayPlugin.FromConfig(config);
                try
                {
                    var script = (IsFileUrl(config.AbsoluteScriptUrl)) ?
                        System.IO.File.ReadAllText(config.AbsoluteScriptUrl.Substring("file:///".Length)) :
                        _client.GET(config.AbsoluteScriptUrl, new Dictionary<string, string>())?.Body?.AsString();

                    _testPlugin = new GrayjayPlugin(new PluginDescriptor(config, encryptedAuth, null, null), script, null, client, clientAuth);
                    _testPlugin.Initialize();
                    lastDevUrl.Save(config.SourceUrl);
                }
                catch(Exception ex)
                {
                    Logger.e(nameof(DeveloperController), ex.Message, ex);
                    _testPlugin = null;
                    throw;
                }
                return Ok(_testPlugin?.GetPackageVariables());
            }
            catch(Exception ex)
            {
                return StatusCode(500, ex.GetType().Name + ":" + ex.Message);
            }
        }

        [HttpGet]
        public IActionResult PackageGet(string variable)
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                if (string.IsNullOrEmpty(variable))
                    return BadRequest("Missing variable name");

                var pack = _testPlugin?.GetPackageByVariable(variable);
                return File(Encoding.UTF8.GetBytes(GetRemoteObjectOrCreate(pack).Serialize()), "application/json");
            }
            catch(Exception ex)
            {
                return StatusCode(500, ex.GetType().Name + ":" + ex.Message);
            }
        }

        [HttpPost]
        public IActionResult RemoteCall(string id, string method, [FromBody] JsonArray parameters)
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                if (string.IsNullOrEmpty(id))
                    return BadRequest("Missing object id");
                if (string.IsNullOrEmpty(method))
                    return BadRequest("Missing method");
                if (method != "isLoggedIn")
                    Logger.i(nameof(DeveloperController), $"Remote Call [{id}].{method}(...)");

                var remoteObj = GetRemoteObject(id);
                var callResult = remoteObj.Call(method, parameters);
                var json = WrapRemoteResult(callResult, false);
                return File(Encoding.UTF8.GetBytes(json), "application/json");
            }
            catch(TargetInvocationException ex)
            {
                Logger.e(nameof(DeveloperController), $"Error in remote call for {method}: {ex.InnerException.Message}", ex.InnerException);
                return StatusCode(500, ex.InnerException.GetType().Name + ":" + ex.InnerException.Message);
            }
            catch(Exception ex)
            {
                Logger.e(nameof(DeveloperController), $"Error in remote call for {method}: {ex.Message}", ex);
                return StatusCode(500, ex.GetType().Name + ":" + ex.Message);
            }
        }

        [HttpGet]
        public IActionResult RemoteProp(string id, string prop)
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                if (string.IsNullOrEmpty(id))
                    return BadRequest("Missing object id");
                if (string.IsNullOrEmpty(prop))
                    return BadRequest("Missing prop");

                var remoteObj = GetRemoteObject(id);
                var callResult = remoteObj.Prop(prop);
                var json = WrapRemoteResult(callResult, false);
                return File(Encoding.UTF8.GetBytes(json), "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.GetType().Name + ":" + ex.Message);
            }
        }

        private string WrapRemoteResult(object callResult, bool useCached = false)
        {
            if (callResult == null)
                return "null";
            else if (callResult.GetType().IsPrimitive || callResult.GetType() == typeof(string))
                return V8RemoteObject.SerializeObject(callResult);
            else if (callResult is ICollection ce && ce.Count == 0)
                return "[]";
            else if(callResult is HttpJSBytesResponse crbr)
            {
                return V8RemoteObject.SerializeObject(new
                {
                    code = crbr.Code,
                    isOk = crbr.IsOk,
                    header = crbr.Headers,
                    url = crbr.Url,
                    body = (crbr.BodyBytes.Size == 0) ? new byte[0] : crbr.BodyBytes.GetBytes()
                });
            }
            else if (callResult is IEnumerable ci)
            {
                var firstItemType = ci.AsQueryable().ElementType;
                if (firstItemType.IsPrimitive || firstItemType.GetType() == typeof(string))
                    return V8RemoteObject.SerializeObject(ci);
                else
                {
                    return V8RemoteObject.SerializeObject(CreateRemoteObjectArray(ci));
                }
            }
            else if (useCached)
                return GetRemoteObjectOrCreate(callResult)?.Serialize() ?? "null";
            else
                return CreateRemoteObject(callResult)?.Serialize() ?? "null";
        }


        [HttpGet]
        public IActionResult GetDevLogs(int index)
        {
            if (!IsDeveloperMode())
                return NotFound();
            return Ok(StateDeveloper.Instance.GetLogs(index));
        }

        public class ProxyRequest
        {
            public string Url { get; set; }
            public Dictionary<string, string> Headers { get; set; }
        }
        [HttpPost]
        public IActionResult Get(string ct, [FromBody] ProxyRequest req)
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                if(IsFileUrl(req.Url))
                {
                    if (req.Url.StartsWith("file:///"))
                        req.Url = req.Url.Substring("file:///".Length);
                    return Ok(new
                    {
                        Url = req.Url,
                        Code = 200,
                        Body = System.IO.File.ReadAllText(req.Url)
                    });
                }

                var resp = _client.GET(req.Url, req.Headers ?? new Dictionary<string, string>());

                return Ok(new
                {
                    Url = resp.Url,
                    Code = resp.Code,
                    Body = resp.Body.AsString()
                });
            }
            catch(Exception ex)
            {
                return Ok(new
                {
                    Url = req.Url,
                    Body = ex.Message,
                    Code = 500
                });
            }
        }


        [HttpGet]
        public IActionResult IsLoggedIn()
        {
            if (!IsDeveloperMode())
                return NotFound();
            return Ok(_testPluginAuth.Item2 != null && _testPluginAuth.Item1 == _testPlugin?.ID);
        }

        [HttpPost]
        public IActionResult LoadDevPlugin([FromBody]PluginConfig config)
        {
            if (!IsDeveloperMode())
                return NotFound();
            try
            {
                config.IconUrl = GrayjayServer.Instance.BaseUrl + "/web/src/assets/favicon.png";
                string script = null;
                if (IsFileUrl(config.AbsoluteScriptUrl))
                {
                    string path = config.AbsoluteScriptUrl;
                    if (config.AbsoluteScriptUrl.StartsWith("file:///"))
                        path = config.AbsoluteScriptUrl.Substring("file:///".Length);
                    script = System.IO.File.ReadAllText(path);
                }
                else
                {
                    var resp = _client.GET(config.AbsoluteScriptUrl, new Dictionary<string, string>());
                    if (!resp.IsOk)
                        return BadRequest($"URL {config.ScriptUrl} return code {resp.Code}");
                    if(resp.Body == null)
                        return BadRequest($"URL {config.ScriptUrl} return no body");
                    script = resp.Body.AsString();
                }
                string devId = StatePlatform.InjectDevPlugin(config, script);
                return Ok(devId);
            }
            catch(Exception ex)
            {
                return BadRequest(ex);
            }
        }





        private bool IsDeveloperMode()
        {
            return GrayjayDevSettings.Instance.DeveloperMode;
        }
        private static byte[] ReadResource(string name)
        {
            var fileName = name;
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream(fileName);

            if (stream == null)
            {
                var resources = assembly.GetManifestResourceNames();
                throw new FileNotFoundException("Cannot find resource file: " + name, fileName);
            }

            using (MemoryStream str = new MemoryStream())
            {
                using (stream)
                {
                    stream.CopyTo(str);
                    return str.ToArray();
                }
            }
        }


        private bool IsFileUrl(string url)
        {
            return !url.ToLower().StartsWith("http://") && !url.ToLower().StartsWith("https://");
        }


        private List<V8RemoteObject> CreateRemoteObjectArray(IEnumerable objs)
        {
            var remotes = new List<V8RemoteObject>();
            foreach (var obj in objs)
                remotes.Add(CreateRemoteObject(obj));
            return remotes;
        }
        private V8RemoteObject CreateRemoteObject<T>(T obj)
        {
            if (obj == null)
                return null;
            var id = Guid.NewGuid().ToString();
            var robj = new V8RemoteObject(id, obj, _testPlugin);
            if (robj.RequiresRegistration)
            {
                lock (_testPluginVariables)
                {
                    _testPluginVariables[id] = robj;
                }
            }
            return robj;
        }
        private V8RemoteObject GetRemoteObjectOrCreate<T>(T obj)
        {
            if (obj == null)
                return null;
            var instance = GetRemoteObjectByInstance(obj);
            if (instance == null)
                instance = CreateRemoteObject(obj);
            return instance;
        }
        private V8RemoteObject GetRemoteObject(string id)
        {
            lock (_testPluginVariables)
            {
                if (!_testPluginVariables.ContainsKey(id))
                    throw new ArgumentException($"Remote object [{id}] does not exist");
                return _testPluginVariables[id];
            }
        }
        private V8RemoteObject GetRemoteObjectByInstance(object obj)
        {
            lock (_testPluginVariables)
            {
                return _testPluginVariables.Values.FirstOrDefault(x => x.Obj == obj);
            }
        }
    }
}
