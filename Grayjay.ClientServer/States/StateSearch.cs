using Grayjay.ClientServer.Store;

namespace Grayjay.ClientServer.States;

public class StateSearch
{
    private class SearchEntry
    {
        public required string Query { get; init; }
        public required long UnixTime { get; set; }
    }

    private readonly ManagedStore<SearchEntry> _previousSearches = new ManagedStore<SearchEntry>("previousSearches_0")
        .WithUnique(x => x.Query)
        .Load();

    public List<string> PreviousSearches
    {
        get => _previousSearches.GetObjects().OrderByDescending(v => v.UnixTime).Select(v => v.Query).ToList();
    }

    public void AddPreviousSearch(string query)
    {
        _previousSearches.CreateOrUpdate(v => v.Query, query, 
            () => new SearchEntry { Query = query, UnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, 
            (v) => v.UnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public void RemovePreviousSearch(string query)
    {
        _previousSearches.DeleteBy(v => v.Query, query);
    }

    public void RemoveAllPreviousSearches()
    {
        _previousSearches.DeleteAll();
    }

    private static readonly object _instanceLock = new object();
    private static StateSearch? _instance = null;
    public static StateSearch Instance
    {
        get
        {
            lock (_instanceLock)
            {
                if (_instance == null)
                    _instance = new StateSearch();
                return _instance;
            }
        }
    }
}