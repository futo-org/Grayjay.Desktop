﻿
namespace Grayjay.ClientServer
{
    public interface IWindowProvider
    {
        Task<IWindow> CreateWindowAsync(string url, string title, int preferredWidth, int preferredHeight, int minimumWidth = 0, int minimumHeight = 0);
        Task<IWindow> CreateInterceptorWindowAsync(string title, string url, string userAgent, bool useMobileEmulation, string? injectJs, Action<InterceptorRequest> handler, CancellationToken cancellationToken = default);
        Task<string>? ShowDirectoryDialogAsync(CancellationToken cancellationToken = default);
        Task<string?> ShowFileDialogAsync((string name, string pattern)[] filters, CancellationToken cancellationToken = default);
        Task<string?> ShowSaveFileDialogAsync(string name, (string name, string pattern)[] filters, CancellationToken cancellationToken = default);
    }

    public interface IWindow
    {
        event Action OnClosed;

        Task SetRequestProxyAsync(string url, Func<WindowRequest, Task<WindowResponse>> handler, CancellationToken cancellationToken = default);

        Task CloseAsync(CancellationToken cancellationToken = default);
    }

    public class InterceptorRequest
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public Dictionary<string, List<string>> Headers { get; set; } = new Dictionary<string, List<string>>(StringComparer.InvariantCultureIgnoreCase);
    }

    public class WindowRequest
    {
        public required string Method { get; set; }
        public required string Url { get; set; }
        public required Dictionary<string, List<string>> Headers { get; set; } = new Dictionary<string, List<string>>(StringComparer.InvariantCultureIgnoreCase);
    }
    public class WindowResponse
    {
        public required int StatusCode { get; init; }
        public required string StatusText { get; init; }
        public required Dictionary<string, List<string>> Headers { get; init; } = new Dictionary<string, List<string>>(StringComparer.InvariantCultureIgnoreCase);
        public required Stream? BodyStream { get; init; }
    }
}
