using Grayjay.Engine.V8;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using Grayjay.Desktop.POC;
using Microsoft.ClearScript;
using Grayjay.Engine;
using Microsoft.ClearScript.JavaScript;
using Newtonsoft.Json.Serialization;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Developer
{
    [JsonConverter(typeof(V8RemoteObject.Serializer))]
    public class V8RemoteObject
    {
        private GrayjayPlugin _plugin;
        private string _id;
        private Type _class;
        public object Obj { get; }
        public bool RequiresRegistration { get; }

        public V8RemoteObject(string id, object obj, GrayjayPlugin plugin = null)
        {
            _plugin = plugin;
            _id = id;
            _class = obj.GetType();
            Obj = obj;
            RequiresRegistration = GetV8Functions(_class).Any() || GetV8Properties(_class).Any();
        }

        public object Prop(string propName)
        {
            var propMethod = GetV8Property(_class, propName);
            return propMethod.Invoke(Obj, new object[] { });
        }

        public object Call(string methodName, JsonArray array)
        {
            var method = GetV8Function(_class, methodName);

            var parameters = method.GetParameters();
            var arguments = new object[parameters.Length];
            var instanceParaCount = 0;

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.Name == "instance")
                {
                    arguments[i] = Obj;
                    instanceParaCount++;
                }
                else if (i - instanceParaCount < array.Count)
                {
                    if (parameter.ParameterType == typeof(ScriptObject) || parameter.ParameterType == typeof(IScriptObject) || parameter.ParameterType == typeof(IJavaScriptObject))
                        arguments[i] = _plugin.GetUnderlyingEngine().Evaluate("(" + array[i - instanceParaCount].ToJsonString() + ")");
                    else
                        arguments[i] = JsonConvert.DeserializeObject(array[i - instanceParaCount].ToJsonString(), parameter.ParameterType);
                }
            }

            return method.Invoke(Obj, arguments);
        }

        public string Serialize()
        {
            return SerializeObject(this);
        }

        public static string SerializeObject(object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented, new JsonSerializerSettings()
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }

        public class Serializer : JsonConverter<V8RemoteObject>
        {
            public override void WriteJson(JsonWriter writer, V8RemoteObject value, JsonSerializer serializer)
            {
                try
                {
                    if (value == null)
                    {
                        writer.WriteNull();
                        return;
                    }

                    if (!value.RequiresRegistration)
                    {
                        serializer.Serialize(writer, value.Obj);
                    }
                    else
                    {
                        var obj = JObject.FromObject(value.Obj, serializer);
                        obj.Add("__id", value._id);

                        var methodsArray = new JArray();
                        foreach (var method in GetV8Functions(value._class))
                        {
                            var scriptMethodAttr = method.GetCustomAttribute<ScriptMemberAttribute>();
                            methodsArray.Add(scriptMethodAttr?.Name ?? method.Name);
                        }
                        obj.Add("__methods", methodsArray);

                        var propsArray = new JArray();
                        foreach (var prop in GetV8Properties(value._class))
                        {
                            var scriptMethodAttr = prop.GetCustomAttribute<ScriptMemberAttribute>();
                            propsArray.Add(scriptMethodAttr?.Name ?? prop.Name);
                        }
                        obj.Add("__props", propsArray);

                        obj.WriteTo(writer);
                    }
                }
                catch (StackOverflowException ex)
                {
                    var msg = $"Recursive structure for class [{value._class.Name}], can't serialize..: {ex.Message}";
                    Logger.e("V8RemoteObject", msg);
                    throw new ArgumentException(msg);
                }
            }

            public override V8RemoteObject ReadJson(JsonReader reader, Type objectType, V8RemoteObject existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.Indented
        };

        private static readonly JsonSerializer _gson = JsonSerializer.CreateDefault(_jsonSettings);
        private static readonly Dictionary<Type, List<MethodInfo>> _classV8Functions = new();
        private static readonly Dictionary<Type, List<MethodInfo>> _classV8Props = new();

        public static List<MethodInfo> GetV8Functions(Type clazz)
        {
            if (!_classV8Functions.ContainsKey(clazz))
                _classV8Functions[clazz] = clazz.GetMethods().Where(m => m.GetCustomAttributes(typeof(ScriptMemberAttribute), false).Any()).ToList();
            return _classV8Functions[clazz];
        }

        public static MethodInfo GetV8Function(Type clazz, string name)
        {
            var methods = GetV8Functions(clazz);
            var method = methods.FirstOrDefault(m => m.Name == name || m.GetCustomAttribute<ScriptMemberAttribute>()?.Name == name);
            if (method == null)
                throw new ArgumentException($"Non-existent function {name}");
            return method;
        }

        public static List<MethodInfo> GetV8Properties(Type clazz)
        {
            if (!_classV8Props.ContainsKey(clazz))
                _classV8Props[clazz] = clazz.GetProperties().Where(m => m.GetCustomAttributes(typeof(ScriptMemberAttribute), false).Any()).Select(x=>x.GetMethod).ToList();
            return _classV8Props[clazz];
        }

        public static MethodInfo GetV8Property(Type clazz, string name)
        {
            var properties = GetV8Properties(clazz);
            var property = properties.FirstOrDefault(m => m.Name == name || m.GetCustomAttribute<ScriptMemberAttribute>()?.Name == name);
            if (property == null)
                throw new ArgumentException($"Non-existent property {name}");
            return property;
        }

        public static string SerializeList(List<V8RemoteObject> objects)
        {
            return JsonConvert.SerializeObject(objects);
        }
    }
}
