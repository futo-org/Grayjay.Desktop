namespace Grayjay.ClientServer.Exceptions
{
    public class DownloadException: Exception
    {
        public bool IsRetryable { get; set; }

        public DownloadException(string message, bool retryable): base(message)
        {
            IsRetryable = retryable;
        }
    }
}
