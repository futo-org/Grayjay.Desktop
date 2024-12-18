
namespace Grayjay.ClientServer
{
    public interface IWindowProvider
    {
        Task<IWindow> CreateWindow(string title, int width, int height, string url, int maxWidth = 0, int maxHeight = 0);
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
