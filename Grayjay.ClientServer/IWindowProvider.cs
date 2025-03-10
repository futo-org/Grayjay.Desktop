
namespace Grayjay.ClientServer
{
    public interface IWindowProvider
    {
        Task<IWindow> CreateWindowAsync(string url, string title, int preferredWidth, int preferredHeight, int minimumWidth = 0, int minimumHeight = 0);
        Task<IWindow> CreateInterceptorWindowAsync(string title, string url, string userAgent, Action<InterceptorRequest> handler, CancellationToken cancellationToken = default);
        Task<string> ShowDirectoryDialogAsync(CancellationToken cancellationToken = default);
        Task<string> ShowFileDialogAsync((string name, string pattern)[] filters, CancellationToken cancellationToken = default);
        Task<string> ShowSaveFileDialogAsync(string name, (string name, string pattern)[] filters, CancellationToken cancellationToken = default);
    }

    public interface IWindow
    {
        event Action OnClosed;

        void Close();
    }

    public class InterceptorRequest
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}
