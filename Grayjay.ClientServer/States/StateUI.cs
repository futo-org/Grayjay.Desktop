using Grayjay.ClientServer.Models;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Grayjay.ClientServer.Controllers.StateUI;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Controllers
{
    public static class StateUI
    {
        public static ConcurrentDictionary<string, Action<DialogResponse>> _handlers = new ConcurrentDictionary<string, Action<DialogResponse>>();

        public static ConcurrentDictionary<string, Action<string, JsonElement>> _handlersCustom = new ConcurrentDictionary<string, Action<string, JsonElement>>();


        public static void Toast(string title, string text)
        {
            GrayjayServer.Instance.WebSocket.Broadcast(new ToastDescriptor(title, text), "Toast");
        }
        public static void Toast(string text)
        {
            if(GrayjayServer.Instance != null)
                GrayjayServer.Instance.WebSocket.Broadcast(new ToastDescriptor(text), "Toast");
        }



        public static async Task MultiDialog(List<DialogDescriptor> descriptors, Action final)
        {
            if((descriptors?.Count ?? 0) == 0)
            {
                final?.Invoke();
                return;
            }

            if (descriptors[0] == null || !descriptors[0].ShouldShow())
            {
                MultiDialog(descriptors.Skip(1).ToList(), final);
                return;
            }
            var currentDialog = descriptors[0];

            currentDialog.Actions = currentDialog.Actions.Select(x => new DialogAction()
            {
                Text = x.Text,
                Action = (r) =>
                {
                    x.Action?.Invoke(r);
                    _ = MultiDialog(descriptors.Skip(1).ToList(), final);
                }
            }).ToList();
            var resp = await Dialog(currentDialog);
            if (resp.Button >= 0 && resp.Button < currentDialog.Actions.Count)
                currentDialog.Actions[resp.Button].Action(resp);
        }

        public static Task<DialogResponse> Dialog(string icon, string text, string textDetails, string code, int defaultCloseAction, params DialogAction[] actions)
        {
            var descriptor = new DialogDescriptor()
            {
                Icon = icon,
                Text = text,
                TextDetails = textDetails,
                Code = code,
                DefaultCloseAction = defaultCloseAction,
                Actions = actions.ToList()
            };
            return Dialog(descriptor);
        }
        public static Task<DialogResponse> Dialog(DialogDescriptor descriptor)
        {
            var completionResult = new TaskCompletionSource<DialogResponse>();

            string id = Guid.NewGuid().ToString();
            _handlers.TryAdd(id, (res) =>
            {
                if (res.Button != -1 && descriptor.Actions.Count > res.Button && descriptor.Actions[res.Button].Action != null)
                    descriptor.Actions[res.Button].Action(res);
                completionResult.SetResult(res);
            });
            GrayjayServer.Instance.WebSocket.Broadcast(descriptor, "Dialog", id).Wait();
            return completionResult.Task;
        }

        public static async Task<DialogResponse> DialogError(string title, string msg, string code = null, Action onOk = null)
        {
            return await Dialog(new DialogDescriptor()
            {
                Icon = "/assets/icons/icon_error.svg",
                Text = title,
                TextDetails = msg,
                Code = code,
                Actions = new List<DialogAction>()
                {
                    new DialogAction("Ok", ()=>onOk?.Invoke())
                }
            });
        }
        public static async Task<DialogResponse> DialogError(string title, Exception ex, Action onOk = null)
        {
            return await Dialog(new DialogDescriptor()
            {
                Icon = "/assets/icons/icon_error.svg",
                Text = title,
                TextDetails = ex.Message,
                Code = null,
                Actions = new List<DialogAction>()
                {
                    new DialogAction("Ok", ()=>onOk?.Invoke())
                }
            });
        }


        public static bool RespondDialog(string id, DialogResponse resp, bool withoutClear = true)
        {
            Action<DialogResponse> handler = null;
            if(_handlers.TryRemove(id, out handler) && handler != null)
            {
                handler(resp);
                return true;
            }
            return false;
        }
        public static bool RespondDialogCustom(string id, string action, JsonElement obj)
        {
            Action<string, JsonElement> handler = null;
            if (_handlersCustom.TryGetValue(id, out handler) && handler != null)
            {
                handler(action, obj);
                return true;
            }
            return false;
        }

        public static async Task<CustomDialog> DialogCustom(string name, Dictionary<string, object> data, Dictionary<string, Action<CustomDialog, JsonElement>> actions = null)
        {
            CustomDialog dialog = new CustomDialog(Guid.NewGuid().ToString(), name, data, actions ?? new Dictionary<string, Action<CustomDialog, JsonElement>>());

            await GrayjayServer.Instance.WebSocket.Broadcast(dialog, "CustomDialog", dialog.ID);

            return dialog;
        }
        public static async Task<CustomDialog> DialogCustom(string name, dynamic data, Dictionary<string, Action<CustomDialog, JsonElement>> actions = null)
        {
            return await DialogCustom(name, new RouteValueDictionary(data).ToDictionary()!, actions);
        }
        public static async Task<CustomDialog> DialogCustom(string name, dynamic data, params (string, Action<CustomDialog, JsonElement>)[] actions)
        {
            return await DialogCustom(name, new RouteValueDictionary(data).ToDictionary()!, actions);
        }

        public class CustomDialog: IDisposable
        {
            private object _updateLock = new object();

            public string ID { get; set; }
            public string Name { get; set; }
            public Dictionary<string, object> Data { get; set; }

            public event Action<CustomDialog, string, JsonElement> OnAction;
            public event Action<CustomDialog> OnClose;

            private Dictionary<string, Action<CustomDialog, JsonElement>> _actions = null;

            public CustomDialog(string id, string name, Dictionary<string, object> initialData, Dictionary<string, Action<CustomDialog, JsonElement>> actions = null)
            {
                ID = id;
                Name = name;
                Data = initialData;

                _handlersCustom[ID] = (action, obj) =>
                {
                    OnAction?.Invoke(this, action, obj);

                    switch (action)
                    {
                        case "close":
                            OnClose?.Invoke(this);
                            break;
                    }
                };

                _actions = actions ?? new Dictionary<string, Action<CustomDialog, JsonElement>>();
                OnAction += HandleAction;
            }

            private void HandleAction(CustomDialog dialog, string action, JsonElement obj)
            {
                if(_actions.ContainsKey(action))
                {
                    _actions[action](dialog, obj);
                }
            }

            public async Task UpdateData(Dictionary<string, object> data)
            {
                Data = data;
                await GrayjayServer.Instance.WebSocket.Broadcast(Data, "CustomDialogUpdate", ID);
            }
            public async Task UpdateData(dynamic data)
            {
                Data = new RouteValueDictionary(data).ToDictionary();
                await GrayjayServer.Instance.WebSocket.Broadcast(Data, "CustomDialogUpdate", ID);
            }

            public async Task Close()
            {
                await GrayjayServer.Instance.WebSocket.Broadcast("", "CustomDialogClose", ID);
            }
            public void Dispose()
            {
                _handlersCustom.TryRemove(ID, out _);
            }
        }


        public class DialogDescriptor
        {
            public string Icon { get; set; }
            public string Text { get; set; }
            public string TextDetails { get; set; }
            public string Code { get; set; }
            public int DefaultCloseAction { get; set; }
            public List<DialogAction> Actions { get; set; }

            [JsonIgnore]
            public Func<bool> ShouldShow { get; private set; } = () => true;

            public DialogDescriptor WithCondition(Func<bool> shouldShow)
            {
                ShouldShow = shouldShow;
                return this;
            }
        }
        public class DialogResponse
        {
            public string Action { get; set; } = null;
            public int Button { get; set; } = -1;
            public int Index { get; set; } = -1;
            public object Selected { get; set; } = null;
            public string Text { get; set; } = null;
        }

        public class DialogAction
        {
            public string Text { get; set; }
            [JsonIgnore]
            public Action<DialogResponse> Action { get; set; }
            ActionStyle Style { get; set; }


            public DialogAction() { }
            public DialogAction(string text, Action action, ActionStyle style = ActionStyle.None)
            {
                Text = text;
                Action = (_)=>action();
                Style = style;
            }
            public DialogAction(string text, Action<DialogResponse> action, ActionStyle style = ActionStyle.None)
            {
                Text = text;
                Action = action;
                Style = style;
            }
        }
        public enum ActionStyle
        {
            None,
            Primary,
            Accent,
            Dangerous,
            DangerousText
        }


        //Special

        public static async Task<bool> ShowCaptchaWindow(PluginConfig config, ScriptCaptchaRequiredException ex, Action<bool> onCompleted = null)
        {
            if (GrayjayServer.Instance?.WindowProvider == null)
                throw new NotImplementedException("Running headless, captcha only supported in UI application mode");

            var descriptor = StatePlugins.GetPlugin(config.ID);
            var pluginConfig = descriptor.Config;
            var authConfig = pluginConfig.Captcha;

            bool urlFound = string.IsNullOrEmpty(authConfig.CompletionUrl);
            Dictionary<string, Dictionary<string, string>> cookiesFoundMap = new Dictionary<string, Dictionary<string, string>>();

            bool completionUrlExcludeQuery = false;
            string completionUrlToCheck = (string.IsNullOrEmpty(authConfig.CompletionUrl)) ? null : authConfig.CompletionUrl;
            if (completionUrlToCheck != null)
            {
                if (authConfig.CompletionUrl.EndsWith("?*"))
                {
                    completionUrlToCheck = completionUrlToCheck.Substring(0, completionUrlToCheck.Length - 2);
                    completionUrlExcludeQuery = true;
                }
            }

            IWindow window = null;

            bool _didLogIn()
            {
                var cookiesFound = authConfig.CookiesToFind?.All(toFind => cookiesFoundMap.Any(x => x.Value.ContainsKey(toFind))) ?? true;

                return (urlFound && cookiesFound);
            }

            void _loggedIn()
            {
                if (_didLogIn())
                {
                    Logger.i(nameof(SourcesController), "Logged in!");
                    _ = window?.CloseAsync();
                }
            }
            void _closed()
            {
                //Finished
                if (_didLogIn())
                {
                    var plugin = StatePlugins.GetPlugin(config.ID);
                    plugin.SetCaptchaData(new Engine.SourceCaptcha()
                    {
                        CookieMap = cookiesFoundMap
                    });
                    StatePlugins.UpdatePlugin(config.ID, true);
                    onCompleted?.Invoke(true);
                }
                else
                    onCompleted?.Invoke(false);
            }

            string? captchaUrl = null;
            if (authConfig.CaptchaUrl != null)
                captchaUrl = authConfig.CaptchaUrl;
            else if (!string.IsNullOrEmpty(ex.Url))
                captchaUrl = ex.Url;
            else
                throw new NotImplementedException("Unhandable captcha?");

            window = await GrayjayServer.Instance.WindowProvider.CreateInterceptorWindowAsync("Grayjay (Captcha)", captchaUrl, authConfig.UserAgent, (InterceptorRequest request) =>
            {
                try
                {
                    var uri = new Uri(request.Url);
                    string domain = uri.Host;
                    string domainLower = uri.Host.ToLower();

                    if (!urlFound)
                    {
                        if (completionUrlExcludeQuery)
                        {
                            if (request.Url.Contains("?"))
                                urlFound = request.Url.Substring(0, request.Url.IndexOf("?")) == completionUrlToCheck;
                            else
                                urlFound = request.Url == completionUrlToCheck;
                        }
                        else
                            urlFound = request.Url == completionUrlToCheck;
                    }

                    //COOKIES
                    var cookieString = request.Headers.FirstOrDefault(x => x.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)).Value;
                    if (cookieString != null)
                    {
                        var domainParts = domain.Split(".");
                        var cookieDomain = "." + string.Join(".", domainParts.Skip(domainParts.Length - 2));
                        if (pluginConfig == null || pluginConfig.AllowUrls.Any(x => x == "everywhere" || x.ToLower().MatchesDomain(cookieDomain)))
                        {
                            authConfig.CookiesToFind?.ForEach(cookiesToFind =>
                            {
                                var cookies = cookieString.Split(";");
                                foreach (var cookieStr in cookies)
                                {
                                    var cookieSplitIndex = cookieStr.IndexOf("=");
                                    if (cookieSplitIndex <= 0) continue;
                                    var cookieKey = cookieStr.Substring(0, cookieSplitIndex).Trim();
                                    var cookieVal = cookieStr.Substring(cookieSplitIndex + 1).Trim();

                                    if (authConfig.CookiesExclOthers && !cookiesToFind.Contains(cookieKey))
                                        continue;

                                    if (cookiesFoundMap.ContainsKey(cookieDomain))
                                        cookiesFoundMap[cookieDomain][cookieKey] = cookieVal;
                                    else
                                        cookiesFoundMap[cookieDomain] = new Dictionary<string, string>() { { cookieKey, cookieVal } };
                                }
                            });
                        }
                    }

                    if (_didLogIn())
                        _loggedIn();
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(SourcesController), "Login Interceptor failed: " + ex.Message, ex);
                    throw ex;
                }
            });
            window.OnClosed += _closed;

            return true;
        }
    }
}
