using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Feed;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.States;


public class WindowState
{
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