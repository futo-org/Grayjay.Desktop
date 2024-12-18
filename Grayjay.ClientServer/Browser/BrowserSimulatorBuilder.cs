using HtmlAgilityPack;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace Grayjay.ClientServer.Browser
{
    public class BrowserSimulatorBuilder
    {

        public List<string> Requirements { get; private set; } = new List<string>();
        public List<string> Actions { get; private set; } = new List<string>();

        public Dictionary<string, string> Navigator { get; set; } = new Dictionary<string, string>();


        public BrowserSimulatorBuilder WithLocation(string url)
        {
            var uri = new Uri(url);
            AddRequirement("const __location = window.location;");
            DefineProperty("window", "location", JsonSerializer.Serialize(new Dictionary<string, string>()
            {
                { "ancestorOrigins", "__location.ancestorOrigins" },
                { "assign", "()=>__location.assign()" },
                { "hash", "" },
                { "host", uri.Host },
                { "hostname", uri.Host },
                { "href", url },
                { "origin", uri.Scheme + "://" + uri.Host },
                { "pathname", uri.LocalPath },
                { "port", "" },
                { "protocol", uri.Scheme + ":" },
                { "reload", "()=>__location.reload()" },
                { "replace", "()=>__location.replace()" },
                { "search", uri.Query },
                { "toString", "()=>{ return \"" + url + "\"; }" },
            }));
            Log("'Faking location', window.location");
            return this;
        }

        public BrowserSimulatorBuilder WithNavigatorValue(string key, string value)
        {
            Navigator[key] = value;
            return this;
        }
        public BrowserSimulatorBuilder HideGetOwnProptyDescriptos(params string[] values)
        {
            AddAction("""
    const __objectGetOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
    Object.getOwnPropertyDescriptor = function(obj, prop) {
        let val = __objectGetOwnPropertyDescriptor(obj, prop);

""" +
        string.Join("\n", values.Select(x => $"if(prop == '{x}') val = undefined;")) +
"""
        return val;
    };

""");
            return this;
        }
        public BrowserSimulatorBuilder WithFakeMediaDevices()
        {
            AddRequirement("""
                    const __mediaDevices = {
                        async enumerateDevices() {
                            return  [
                               {
                                  "deviceId": "",
                                  "kind": "audioinput",
                                  "label": "",
                                  "groupId": ""
                               },
                               {
                                  "deviceId": "",
                                  "kind": "videoinput",
                                  "label": "",
                                  "groupId": ""
                               },
                               {
                                  "deviceId": "",
                                  "kind": "audiooutput",
                                  "label": "",
                                  "groupId": ""
                               }
                            ];
                        },
                        async getDisplayMedia(){},
                        async getSupportedConstraints(){
                            return {
                               "aspectRatio": true,
                               "autoGainControl": true,
                               "brightness": true,
                               "channelCount": true,
                               "colorTemperature": true,
                               "contrast": true,
                               "deviceId": true,
                               "displaySurface": true,
                               "echoCancellation": true,
                               "exposureCompensation": true,
                               "exposureMode": true,
                               "exposureTime": true,
                               "facingMode": true,
                               "focusDistance": true,
                               "focusMode": true,
                               "frameRate": true,
                               "groupId": true,
                               "height": true,
                               "iso": true,
                               "latency": true,
                               "noiseSuppression": true,
                               "pan": true,
                               "pointsOfInterest": true,
                               "resizeMode": true,
                               "sampleRate": true,
                               "sampleSize": true,
                               "saturation": true,
                               "sharpness": true,
                               "suppressLocalAudioPlayback": true,
                               "tilt": true,
                               "torch": true,
                               "voiceIsolation": true,
                               "whiteBalanceMode": true,
                               "width": true,
                               "zoom": true
                            };
                        },
                        async getUserMedia() {},
                        ondevicechange: null,
                        async setCaptureHandleConfig(){}
                    };
                """);
            WithNavigatorValue("mediaDevices", "__mediaDevices");
            return this;
        }
        public BrowserSimulatorBuilder WithFakeGPU(string unmaskedVendor, string unmaskedRenderer)
        {
            AddAction($"const __glFakeVendor = '{unmaskedVendor}'; const __glFakeRenderer = '{unmaskedRenderer}'" + """
                const __gl = document.createElement("canvas").getContext("webgl");
                    const __gl_debugInfo = __gl.getExtension('WEBGL_debug_renderer_info');
                    const __UNMASKED_VENDOR = __gl_debugInfo.UNMASKED_VENDOR_WEBGL;
                    const __UNMASKED_RENDERER = __gl_debugInfo.UNMASKED_RENDERER_WEBGL;
                    const __WebGLRenderingContextGetParameter = WebGLRenderingContext.prototype.getParameter;
                    WebGLRenderingContext.prototype.getParameter = function(para) {
                        let result = __WebGLRenderingContextGetParameter.apply(this, [para]);
                        if(para == __UNMASKED_VENDOR)
                            result = __glFakeVendor;
                        else if(para == __UNMASKED_RENDERER)
                            result = __glFakeRenderer;
                        console.log("WebGL Accessed getParameter(" + para + "): " + result);
                        return result;
                    }

                """);
            return this;
        }
        public BrowserSimulatorBuilder WithInterceptedPermissions()
        {
            AddAction("""
                    const __permissionsQuery = Permissions.prototype.query;
                    Permissions.prototype.query = function(para) {
                        let result = null;//__permissionsQuery.apply(this, [para]);
                        console.log("Permissions.query(" + para.name + "): [" + result?.name + ", " + result?.state + "]"); 
                        if(true) {
                            result = {
                                name: para.name,
                                state: 'prompt',
                                onchange: null,
                                constructor: PermissionStatus
                            }
                            console.log("Permissions.query(" + para.name + "): Intercepted: " + JSON.stringify(result)); 
                        }
                        return result;
                    }
                """);
            return this;
        }

        public BrowserSimulatorBuilder NavigatorMobile()
        {
            Navigator.Clear();
            AddRequirement(REQ_GET_HIGH_ENTROPY_VALUES);
            AddRequirement(REQ_MOBILE_USER_AGENT_DATA);
            AddRequirement(REQ_MEDIA_DEVICES);
            AddAction("""
    function __getNavigatorValue(target, key) {
                switch(key) {
                    case "webdriver":
                        return false;
                    case "platform":
                        return "Linux armv81"
                    case "constructor":
                        return Navigator.prototype.constructor;
                    case "maxTouchPoints":
                        return 8;
                    case "hardwareConcurrency":
                        return 3;
                    case "keyboard":
                        return null;
                    case "connection":
                        return {
                            downlink: 1.6,
                            downlinkMax: null,
                            effectiveType: "4g",
                            rtt: 50,
                            saveData: false,
                            type: "wifi"
                        };
                    case "cookieEnabled":
                        return true;
                    case "deviceMemory":
                        return 2;
                    case "mediaDevices":
                        return __mediaDevices;
                    case "permissions":
                        return {
                            async query(arg) {
                                console.log("navigator.permissions.query(...)");
                                return target.permissions.query(arg);
                            }
                        };
                    case "languages":
                        return ["en-US"];
                    case "userAgentData":
                        return {
                            "brands":[{"brand":"Chromium","version":"124"},{"brand":"Google Chrome","version":"124"},{"brand":"Not-A.Brand","version":"99"}],
                            "mobile":true,
                            "platform":"Android",
                            "getHighEntropyValues": __getHighEntropyValues
                        };
                }
                if(!target)
                    return undefined;
                return (typeof target[key] === "function") ? target[key].bind(target) : target[key];
    }
""");
            AddAction(ACT_NAVIGATOR_REPLACE);


            return this;
        }



        public BrowserSimulatorBuilder DefineProperty(string parent, string key, string value)
        {
            AddAction($"Object.defineProperty({parent}, \"{key}\", {{ value: {value} }});");
            return this;
        }
        public BrowserSimulatorBuilder Log(string msg)
        {
            AddAction("console.log(" + msg + ")");
            return this;
        }
        public BrowserSimulatorBuilder Stalk(string parent, string property, string context)
        {
            AddRequirement(REQ_STALK);
            AddAction($"__stalk({parent}, \"{property}\", \"{context}\"");
            return this;
        }

        public BrowserSimulatorBuilder AddRequirement(string str)
        {
            if (!Requirements.Contains(str))
            {
                Requirements.Add(str);
            }
            return this;
        }

        public BrowserSimulatorBuilder AddAction(string str)
        {
            if (!Actions.Contains(str))
            {
                Actions.Add(str);
            }
            return this;
        }

        public string Build()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("(()=>{");
            builder.AppendLine(REQ_ALWAYS);
            foreach(var req in Requirements)
            {
                builder.AppendLine(req);
            }
            if(Navigator.Count > 0)
            {
                AddNavigatorCode(Navigator);
            }
            foreach(var act in Actions)
            {
                builder.AppendLine(act);
            }
            builder.AppendLine("})()");
            return builder.ToString();
        }
        public string InjectHtml(string html)
        {
            string built = Build();
            return html.Replace("<head>", "<head><script>\n" + built + "</script>");
        }


        private BrowserSimulatorBuilder AddNavigatorCode(Dictionary<string, string> replaces)
        {
            AddRequirement(REQ_GET_HIGH_ENTROPY_VALUES);
            AddAction("""
    function __getNavigatorValue(target, key) {
                switch(key) {
""" +
    string.Join("\n", replaces.Select(x => $"""
        case "{x.Key}":
            return {x.Value};
""")) +
"""
                }
                if(!target)
                    return undefined;
                return (typeof target[key] === "function") ? target[key].bind(target) : target[key];
    }
""");
            AddAction(ACT_NAVIGATOR_REPLACE);
            return this;
        }

        private const string REQ_ALWAYS = """
    let __logged = [];
    const ignoreProperties = ["navigator", "clientInformation", "window", "document", "location", "top", "chrome", "TrustedScriptURL", "TrustedScript", "TrustedHTML", "TrustedTypePolicy", "TrustedTypePolicyFactory", "trustedTypes", "performance", "_DumpException", "self", "history"];
""";
        private const string REQ_STALK = """
    function __stalk(obj, property, contextName) {
        Object.defineProperty(obj, property, {
            value: new Proxy(navigator, {
                has: (target, key) => {
                    return key in target;
                },
                get: (target, key) => {
                    console.log("Accessed: " + contextName + "." + key);
                    if(!target)
                        return undefined;
                    return (typeof target[key] === "function") ? target[key].bind(target) : target[key];
                }
            }),
        });
    }
""";

        private const string ACT_NAVIGATOR_REPLACE = """
    Object.defineProperty(window, "navigator", {
        value: new Proxy(navigator, {
            has: (target, key) => {
                switch(key) {
                    case "webdriver":
                        return false;

                }
                return key in target;
            },
            get: (target, key) => {
                if(__logged.indexOf("navigator." + key) < 0) {
                    console.log("ACCESSED:navigator." + key);
                    __logged.push("navigator." + key);
                }
                return __getNavigatorValue(target, key);
            }
        }),
    });
    Object.defineProperty(window, "clientInformation", {
        value: new Proxy(navigator, {
            has: (target, key) => {
                switch(key) {
                    case "webdriver":
                        return false;

                }
                return key in target;
            },
            get: (target, key) => {
                if(__logged.indexOf("clientInformation." + key) >= 0) {
                    console.log("ACCESSED:clientInformation." + key);
                    __logged.push("clientInformation." + key);
                }
                return __getNavigatorValue(target, key);
            }
        }),
    });
""";



        private const string REQ_MOBILE_USER_AGENT_DATA = """
    const __userAgentData = {
        architecture: ",
        bitness: ",
        brands: [
            {"brand":"Chromium","version":"124"},
            {"brand":"Google Chrome","version":"124"},
            {"brand":"Not-A.Brand","version":"99"}
        ],
        fullVersionList: [
            {"brand":"Chromium","version":"124.0.0.0"},
            {"brand":"Google Chrome","version":"124.0.0.0"},
            {"brand":"Not-A.Brand","version":"99.0.0.0"}
        ],
        mobile: true,
        model: "",
        platform: "Android",
        platformVersion: "12.0.0",
        uaFullVersion: "124.0.0.0",
        wow64: false
    };
""";
        private const string REQ_MEDIA_DEVICES = """
    const __mediaDevices = {
        async enumerateDevices() {
            return  [
               {
                  "deviceId": "",
                  "kind": "audioinput",
                  "label": "",
                  "groupId": ""
               },
               {
                  "deviceId": "",
                  "kind": "videoinput",
                  "label": "",
                  "groupId": ""
               },
               {
                  "deviceId": "",
                  "kind": "audiooutput",
                  "label": "",
                  "groupId": ""
               }
            ];
        },
        async getDisplayMedia(){},
        async getSupportedConstraints(){
            return {
               "aspectRatio": true,
               "autoGainControl": true,
               "brightness": true,
               "channelCount": true,
               "colorTemperature": true,
               "contrast": true,
               "deviceId": true,
               "displaySurface": true,
               "echoCancellation": true,
               "exposureCompensation": true,
               "exposureMode": true,
               "exposureTime": true,
               "facingMode": true,
               "focusDistance": true,
               "focusMode": true,
               "frameRate": true,
               "groupId": true,
               "height": true,
               "iso": true,
               "latency": true,
               "noiseSuppression": true,
               "pan": true,
               "pointsOfInterest": true,
               "resizeMode": true,
               "sampleRate": true,
               "sampleSize": true,
               "saturation": true,
               "sharpness": true,
               "suppressLocalAudioPlayback": true,
               "tilt": true,
               "torch": true,
               "voiceIsolation": true,
               "whiteBalanceMode": true,
               "width": true,
               "zoom": true
            };
        },
        async getUserMedia() {},
        ondevicechange: null,
        async setCaptureHandleConfig(){}
    };
""";
        private const string REQ_SCREEN = """
    const __screen = {
        availHeight: 833,
        availLeft: 0,
        availTop: 0,
        availWidth: 385,
        colorDepth: 24,
        height: 833,
        isExtended: false,
        orientation: {
            angle: 0,
            onchange: null,
            type: "portrait-primary"
        },
        pixelDepth: 24,
        width: 385
    }
""";
        private const string REQ_GET_HIGH_ENTROPY_VALUES = """
    async function __getHighEntropyValues(arr) {
        const result = {};
        for(let key of arr) {
            if(key in __userAgentData)
                result[key] = __userAgentData[key];
        }
        return result;
    }
""";

    }
}
