using DotCef;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Browser;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.Desktop.CEF
{
    public class CEFWindowProvider : IWindowProvider
    {
        private DotCefProcess _cef;
        public CEFWindowProvider(DotCefProcess process)
        {
            _cef = process;
        }

        public async Task<IWindow> CreateWindowAsync(string url, string title, int preferredWidth, int preferredHeight, int minimumWidth, int minimumHeight)
        {
            var window = await _cef.CreateWindowAsync(
                url: "about:blank",
                minimumWidth: minimumWidth,
                minimumHeight: minimumHeight,
                preferredWidth: preferredWidth,
                preferredHeight: preferredHeight,
                title: title, 
                iconPath: Path.GetFullPath("grayjay.png")
            );

            await window.SetDevelopmentToolsEnabledAsync(true);
            await window.LoadUrlAsync($"{GrayjayServer.Instance.BaseUrl}/web/index.html");
            await window.WaitForExitAsync(CancellationToken.None);

            return new Window(window);
        }


        public async Task<string> ShowDirectoryDialogAsync(CancellationToken cancellationToken = default)
        {
            return await _cef.PickDirectoryAsync(cancellationToken);
        }
        public async Task<string> ShowFileDialogAsync((string name, string pattern)[] filters, CancellationToken cancellationToken = default)
        {
            return (await _cef.PickFileAsync(false, filters, cancellationToken)).First();
        }
        public async Task<string> ShowSaveFileDialogAsync(string defaultName, (string name, string pattern)[] filters, CancellationToken cancellationToken = default)
        {
            return (await _cef.SaveFileAsync(defaultName, filters, cancellationToken));
        }

        private string EvaluateScriptParameter(string source)
        {
            return "{ \"source\":" + JsonSerializer.Serialize(source) + "}";
        }
        public async Task<IWindow> CreateInterceptorWindowAsync(string title, string url, string userAgent, Action<InterceptorRequest> handler, CancellationToken cancellationToken = default)
        {
            //userAgent = "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";
            //double scale = 1.25;
            var window = await _cef.CreateWindowAsync(
                url: "about:blank", 
                minimumWidth: 385, 
                minimumHeight: 833, 
                preferredWidth: 385, 
                preferredHeight: 833, 
                title: title, 
                iconPath: Path.GetFullPath("grayjay.png"), 
                developerToolsEnabled: true, 
                modifyRequests: true,
                resizable: false,
                requestModifier: (window, req) =>
                {
                    foreach(var header in req.Headers.ToList())
                    {
                        if (header.Key.ToLower().StartsWith("sec-"))
                            req.Headers.Remove(header.Key);
                    }
                    req.Headers.Add("Sec-GPC", "1");
                    if(req.Url.Contains("batch"))
                    {
                        string isBatch = "";
                    }
                    handler(new InterceptorRequest()
                    {
                        Url = req.Url,
                        Method = req.Method,
                        Headers = req.Headers
                    });
                    return req;
                }, cancellationToken: cancellationToken);
            await window.SetDevelopmentToolsEnabledAsync(true);
            if (true)
            {
                await window.ExecuteDevToolsMethodAsync("Page.enable", "{}");
                
                await window.ExecuteDevToolsMethodAsync("Page.addScriptToEvaluateOnNewDocument", EvaluateScriptParameter("""
                (()=>{
                    const __userAgentData = {
                        architecture: "",
                        bitness: "",
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
                    async function __getHighEntropyValues(arr) {
                        const result = {};
                        for(let key of arr) {
                            if(key in __userAgentData)
                                result[key] = __userAgentData[key];
                        }
                        return result;
                    }
                
                    console.log("MODIFIED START");
                    let __logged = [];
                    const ignoreProperties = ["navigator", "clientInformation", "window", "document", "location", "top", "chrome", "TrustedScriptURL", "TrustedScript", "TrustedHTML", "TrustedTypePolicy", "TrustedTypePolicyFactory", "trustedTypes", "performance", "_DumpException", "self", "history"]

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
                    __stalk(window, "ontouchstart", "window.ontouchstart");
                
                    const __gl = document.createElement("canvas").getContext("webgl");
                    const __gl_debugInfo = __gl.getExtension('WEBGL_debug_renderer_info');
                    const __UNMASKED_VENDOR = __gl_debugInfo.UNMASKED_VENDOR_WEBGL;
                    const __UNMASKED_RENDERER = __gl_debugInfo.UNMASKED_RENDERER_WEBGL;
                    const __WebGLRenderingContextGetParameter = WebGLRenderingContext.prototype.getParameter;
                    WebGLRenderingContext.prototype.getParameter = function(para) {
                        let result = __WebGLRenderingContextGetParameter.apply(this, [para]);
                        if(para == __UNMASKED_VENDOR)
                            result = "Google Inc. (Qualcomm)";
                        else if(para == __UNMASKED_RENDERER)
                            result = "ANGLE (Qualcomm, Adreno (TM) 640, OpenGL ES 3.2)";
                        console.log("WebGL Accessed getParameter(" + para + "): " + result);
                        return result;
                    }
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
                    const __objectGetOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
                    Object.getOwnPropertyDescriptor = function(obj, prop) {
                        let val = __objectGetOwnPropertyDescriptor(obj, prop);
                        if(prop == 'webdriver')
                            val = undefined;
                        return val;
                    };
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
                    /*
                    Object.defineProperty(window, "devicePixelRatio", { value: 3.75 });
                    Object.defineProperty(window, "innerWidth", { value: 1364 });
                    Object.defineProperty(window, "innerHeight", { value: 2259 });
                    Object.defineProperty(window, "outerWidth", { value: 384 });
                    Object.defineProperty(window, "outerHeight", { value: 636 });
                    Object.defineProperty(window, "screen", { value: __screen });*/
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
                    console.log("MODIFIED2");
                })();
                """));
            }

            if (!string.IsNullOrEmpty(userAgent))
                window.ExecuteDevToolsMethodAsync("Network.setUserAgentOverride", "{\"userAgent\": \"" + userAgent + "\"}");
            
            await window.LoadUrlAsync(url);
            Logger.i(nameof(CEFWindowProvider), "Window created.");

            return new Window(window);
        }

        public class Window : IWindow
        {
            private DotCefWindow _window;

            public event Action OnClosed;

            public Window(DotCefWindow window)
            {
                _window = window;
                _window.OnClose += () =>
                {
                    OnClosed?.Invoke();
                };
            }

            public void Close()
            {
                _window.CloseAsync();
            }
        }
    }
}
