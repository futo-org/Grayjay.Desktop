using DotCef;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Browser;
using Grayjay.ClientServer.Dialogs;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Collections.Concurrent;
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
                iconPath: Utilities.FindFile("grayjay.png")
            );

            await window.SetDevelopmentToolsEnabledAsync(true);
            await window.LoadUrlAsync($"{GrayjayServer.Instance.BaseUrl}/web/index.html");
            await window.WaitForExitAsync(CancellationToken.None);

            return new Window(window);
        }


        public async Task<string?> ShowDirectoryDialogAsync(CancellationToken cancellationToken = default)
        {
            var taskCompletionSource = new TaskCompletionSource<string?>();
            try
            {
                var dialog = FilePickerDialog.OpenFolderPicker((v) => taskCompletionSource.SetResult(v.FirstOrDefault()), false, null);
                await dialog.Show();
                return await taskCompletionSource.Task;
            }
            catch (Exception e)
            {
                taskCompletionSource.SetException(e);
                throw;
            }
            //return await _cef.PickDirectoryAsync(cancellationToken);
        }
        public async Task<string?> ShowFileDialogAsync((string name, string pattern)[] filters, CancellationToken cancellationToken = default)
        {
            var taskCompletionSource = new TaskCompletionSource<string?>();
            try
            {
                var dialog = FilePickerDialog.OpenFilePicker((v) => taskCompletionSource.SetResult(v.FirstOrDefault()), false, filters.Select(v => new FilePickerDialog.Filter()
                {
                    Name = v.name,
                    Pattern = v.pattern
                }).ToArray());
                await dialog.Show();
                return await taskCompletionSource.Task;
            }
            catch (Exception e)
            {
                taskCompletionSource.SetException(e);
                throw;
            }
            //return (await _cef.PickFileAsync(false, filters, cancellationToken)).First();
        }
        public async Task<string?> ShowSaveFileDialogAsync(string defaultName, (string name, string pattern)[] filters, CancellationToken cancellationToken = default)
        {
            var taskCompletionSource = new TaskCompletionSource<string?>();
            try
            {
                var dialog = FilePickerDialog.SaveFilePicker((v) => taskCompletionSource.SetResult(v.FirstOrDefault()), defaultName, filters.Select(v => new FilePickerDialog.Filter()
                {
                    Name = v.name,
                    Pattern = v.pattern
                }).ToArray());
                await dialog.Show();
                return await taskCompletionSource.Task;
            }
            catch (Exception e)
            {
                taskCompletionSource.SetException(e);
                throw;
            }
            //return (await _cef.SaveFileAsync(defaultName, filters, cancellationToken));
        }

        private string EvaluateScriptParameter(string source)
        {
            return "{ \"source\":" + JsonSerializer.Serialize(source) + "}";
        }
        public async Task<IWindow> CreateInterceptorWindowAsync(string title, string url, string userAgent, bool useMobileEmulation, string injectJs, Action<InterceptorRequest> handler, CancellationToken cancellationToken = default)
        {
            //userAgent = "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";
            //double scale = 1.25;
            var window = await _cef.CreateWindowAsync(
                url: "about:blank", 
                minimumWidth: 385, 
                minimumHeight: 833, 
                preferredWidth: useMobileEmulation ? 385 : 1024, 
                preferredHeight: useMobileEmulation ? 833 : 800, 
                title: title, 
                iconPath: Utilities.FindFile("grayjay.png"), 
                developerToolsEnabled: true, 
                modifyRequests: true,
                //resizable: false,
                requestModifier: (window, req) =>
                {
                    foreach (var header in req.Headers.ToList())
                    {
                        if (header.Key.ToLower().StartsWith("sec-"))
                            req.Headers.Remove(header.Key);
                    }
                    req.Headers.Add("Sec-GPC", [ "1" ]);
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
                if (useMobileEmulation)
                {
                    await window.ExecuteDevToolsMethodAsync("Page.addScriptToEvaluateOnNewDocument", EvaluateScriptParameter("""
                    (() => {
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
                        const __gl = document.createElement("canvas").getContext("webgl");
                        if (__gl) {
                            const __gl_debugInfo = __gl.getExtension('WEBGL_debug_renderer_info');
                            if (__gl_debugInfo) {
                                const __UNMASKED_VENDOR = __gl_debugInfo.UNMASKED_VENDOR_WEBGL;
                                const __UNMASKED_RENDERER = __gl_debugInfo.UNMASKED_RENDERER_WEBGL;
                                const __WebGLRenderingContextGetParameter = WebGLRenderingContext.prototype.getParameter;
                                WebGLRenderingContext.prototype.getParameter = function(para) {
                                    let result = __WebGLRenderingContextGetParameter.apply(this, [para]);
                                    if(para === __UNMASKED_VENDOR)
                                        result = "Google Inc. (Qualcomm)";
                                    else if(para === __UNMASKED_RENDERER)
                                        result = "ANGLE (Qualcomm, Adreno (TM) 640, OpenGL ES 3.2)";
                                    return result;
                                }
                            }
                        }
                        const __gl2 = document.createElement("canvas").getContext("webgl2");
                        if (__gl2) {
                            const __gl_debugInfo2 = __gl2.getExtension('WEBGL_debug_renderer_info');
                            if (__gl_debugInfo2) {
                                const __UNMASKED_VENDOR2 = __gl_debugInfo2.UNMASKED_VENDOR_WEBGL;
                                const __UNMASKED_RENDERER2 = __gl_debugInfo2.UNMASKED_RENDERER_WEBGL;
                                const __WebGL2RenderingContextGetParameter = WebGL2RenderingContext.prototype.getParameter;
                                WebGL2RenderingContext.prototype.getParameter = function(para) {
                                    let result = __WebGL2RenderingContextGetParameter.apply(this, [para]);
                                    if(para === __UNMASKED_VENDOR2)
                                        result = "Google Inc. (Qualcomm)";
                                    else if(para === __UNMASKED_RENDERER2)
                                        result = "ANGLE (Qualcomm, Adreno (TM) 640, OpenGL ES 3.2)";
                                    return result;
                                }
                            }
                        }
                        const __permissionsQuery = Permissions.prototype.query;
                        Permissions.prototype.query = function(para) {
                            const result = {
                                name: para.name,
                                state: 'prompt',
                                onchange: null,
                                constructor: PermissionStatus
                            };
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
                                    return "Linux armv8l";
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
                                    return __getNavigatorValue(target, key);
                                }
                            }),
                        });
                        delete window.webkitRequestFileSystem;
                        delete window.webkitResolveLocalFileSystemURL;
                        delete window.webkitSpeechRecognitionError;
                        delete window.webkitSpeechRecognitionEvent;
                    })();
                    """));
                }
            }

            if (!string.IsNullOrWhiteSpace(injectJs))
            {
                await window.ExecuteDevToolsMethodAsync("Page.addScriptToEvaluateOnNewDocument", EvaluateScriptParameter(injectJs));
            }

            if (!string.IsNullOrEmpty(userAgent))
                await window.ExecuteDevToolsMethodAsync("Network.setUserAgentOverride", "{\"userAgent\": \"" + userAgent + "\"}");

            
            await window.LoadUrlAsync(url);
            Logger.i(nameof(CEFWindowProvider), "Window created.");

            return new Window(window);
        }

        public class Window : IWindow
        {
            private DotCefWindow _window;

            public event Action OnClosed;

            private ConcurrentDictionary<string, Func<IPCRequest, Task<IPCResponse?>>> _proxyHandlers = new ConcurrentDictionary<string, Func<IPCRequest, Task<IPCResponse?>>>();

            public Window(DotCefWindow window)
            {
                _window = window;
                _window.SetRequestProxy((cef, req) =>
                {
                    if (_proxyHandlers.ContainsKey(req.Url))
                        return _proxyHandlers[req.Url](req);
                    throw new Exception("This should never happen.");
                });
                _window.OnClose += () =>
                {
                    OnClosed?.Invoke();
                };
            }

            public async Task CloseAsync(CancellationToken cancellationToken = default)
            {
                await _window.CloseAsync(cancellationToken: cancellationToken);
            }

            public async Task SetRequestProxyAsync(string url, Func<WindowRequest, Task<WindowResponse>> handler, CancellationToken cancellationToken = default)
            {
                var ipcHandle = (IPCRequest req) =>
                {
                    return handler(new WindowRequest()
                    {
                        Url = req.Url,
                        Headers = req.Headers,
                        Method = req.Method
                    }).ContinueWith<IPCResponse?>(t =>
                    {
                        if(t is Task<WindowResponse> vt)
                        {
                            if (vt.Status == TaskStatus.RanToCompletion)
                            {
                                return new IPCResponse()
                                {
                                    StatusCode = vt.Result.StatusCode,
                                    StatusText = vt.Result.StatusText,
                                    Headers = vt.Result.Headers,
                                    BodyStream = vt.Result.BodyStream
                                };
                            }
                            else
                                throw vt.Exception ?? new Exception("Incomplete but no exception");
                        }
                        return null;
                    });
                };
                _proxyHandlers.AddOrUpdate(url, ipcHandle, (x,v) => { return ipcHandle; });
                await _window.AddUrlToProxyAsync(url, cancellationToken);
            }
        }
    }
}
