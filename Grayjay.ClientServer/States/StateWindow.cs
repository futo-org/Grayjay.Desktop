using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Feed;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.States;


public class WindowState
{
    public bool Ready { get; set; }
    public string WindowID { get; set; }
    public DateTime LastAccess { get; set; } = DateTime.Now;

    public HomeController.HomeState HomeState { get; set; } = new HomeController.HomeState();
    public HistoryController.HistoryState HistoryState { get; set; } = new HistoryController.HistoryState();
    public ChannelController.ChannelState ChannelState { get; set; } = new ChannelController.ChannelState();
    public DetailsController.DetailsState DetailsState { get; set; } = new DetailsController.DetailsState();
    public PlaylistController.PlaylistState PlaylistState { get; set; } = new PlaylistController.PlaylistState();
    public SearchController.SearchState SearchState { get; set; } = new SearchController.SearchState();
    public SubscriptionsController.SubscriptionsState SubscriptionsState { get; set; } = new SubscriptionsController.SubscriptionsState();

    public WindowState(string id)
    {
        WindowID = id;
    }


}

public static class StateWindow
{
    private static string _defaultID = "UNSPECIFIED";
    private static Dictionary<string, WindowState> _states = new Dictionary<string, WindowState>();

    private static bool _firstWindowReady = false;
    private static List<Action> _onFirstReadyWaiters = new List<Action>();

    public static void StateReadyChanged(WindowState state, bool ready)
    {
        if (ready)
        {
            if (_firstWindowReady)
                return;
            lock(_onFirstReadyWaiters)
            {
                _firstWindowReady = true;
                foreach(var  action in _onFirstReadyWaiters)
                {
                    try
                    {
                        action();
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(StateWindow), "First Window Ready handler failed: " + ex.Message, ex);
                    }
                }
            }

        }
    }

    public static Task WaitForReadyAsync()
    {
        TaskCompletionSource src = new TaskCompletionSource();
        WaitForReady(() =>
        {
            src.SetResult();
        });
        return src.Task;
    }
    public static void WaitForReady(Action handle)
    {
        bool wasReady = false;
        lock(_onFirstReadyWaiters)
        {
            wasReady = _firstWindowReady;
            if(!wasReady)
                _onFirstReadyWaiters.Add(handle);
        }
        if (wasReady)
            handle();
    }

    public static List<WindowState> GetAllStates()
    {
        lock (_states)
        {
            return _states.Values.ToList();
        }
    }
    public static WindowState GetState(this HttpContext context)
    {
        string id = (context.Request.Headers.ContainsKey("WindowID") ? context.Request.Headers["WindowID"].FirstOrDefault() : null);
        if(id == null)
        {
            if (context.Request.Query.ContainsKey("windowId"))
                id = context.Request.Query["windowId"].FirstOrDefault();
            else
            {
                Logger.e(nameof(StateWindow), "Attempted to use a backend method that required a window id without id (" + context.Request.Path + ")");
                id = _defaultID;
            }
        }
        WindowState state = null;
        lock (_states)
        {
            if (!_states.ContainsKey(id))
                _states.Add(id, new WindowState(id));
            state = _states[id];
        }
        state.LastAccess = DateTime.Now;
        return state;
    }

    public static WindowState State(this ControllerBase controller)
    {
        return GetState(controller.HttpContext);
    }
}