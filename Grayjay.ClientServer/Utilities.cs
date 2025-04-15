using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer;

public class UrlParseResult
{
    public required string Scheme { get; init; }
    public required string Url { get; init; }
    public required string HostAndPort { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Path { get; init; }
}

public static class Utilities
{
    public static UrlParseResult ParseUrl(string url)
    {
        string urlRemainder;
        string scheme;
        if (url.StartsWith("https://"))
        {
            scheme = "https";
            urlRemainder = url.Substring("https://".Length);
        }
        else if (url.StartsWith("http://"))
        {
            scheme = "http";
            urlRemainder = url.Substring("http://".Length);
        }
        else if (url.StartsWith("file://"))
        {
            scheme = "file";
            urlRemainder = url.Substring("file://".Length);
        }
        else
            throw new InvalidDataException("Not a valid URL.");

        int hostEndIndex = urlRemainder.IndexOf('/');
        string hostAndPort = hostEndIndex == -1 ? urlRemainder : urlRemainder.Substring(0, hostEndIndex);
        string path = hostEndIndex == -1 ? "/" : urlRemainder.Substring(hostEndIndex);                            
        var portSeparatorIndex = hostAndPort.IndexOf(':');
        var host = portSeparatorIndex == -1 ? hostAndPort : hostAndPort.Substring(0, portSeparatorIndex);
        var port = portSeparatorIndex == -1 ? (scheme == "http" ? 80 : 443) : int.Parse(hostAndPort.Substring(portSeparatorIndex + 1));

        return new UrlParseResult()
        {
            Url = url,
            Scheme = scheme,
            Path = path,
            Port = port,
            Host = host,
            HostAndPort = hostAndPort
        };
    }

    public static async Task<TcpClient?> ConnectAsync(List<IPAddress> addresses, int port, TimeSpan timeout, CancellationToken externalCancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        linkedCts.CancelAfter(timeout);
        var combinedCancellationToken = linkedCts.Token;

        if (addresses == null || addresses.Count == 0)
            return null;

        if (addresses.Count == 1)
            return await TryConnectAsync(addresses[0], port, combinedCancellationToken);

        var tasks = new List<Task<TcpClient?>>();
        foreach (var address in addresses)
            tasks.Add(TryConnectAsync(address, port, combinedCancellationToken));

        while (tasks.Count > 0)
        {
            try
            {
                var completedTask = await Task.WhenAny(tasks).ConfigureAwait(false);
                if (completedTask.Result != null)
                {
                    linkedCts.Cancel();
                    foreach (var task in tasks)
                    {
                        if (task != completedTask && task.Status == TaskStatus.RanToCompletion && task.Result != null)
                            task.Result.Close();
                    }

                    return completedTask.Result;
                }

                tasks.Remove(completedTask);
            }
            catch (OperationCanceledException)
            {
                foreach (var task in tasks)
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                        task.Result.Close();
                }
                throw;
            }
        }

        return null;
    }

    private static async Task<TcpClient?> TryConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            Logger.i(nameof(TryConnectAsync), $"Connecting to {address}:{port}");
            await client.ConnectAsync(address, port).WaitAsync(cancellationToken);
            return client;
        }
        catch (Exception e)
        {
            Logger.i(nameof(TryConnectAsync), $"Failed to connect to {address}:{port} '{e.Message}': {e.StackTrace}");
            client.Close();
            return null;
        }
    }

    public static List<IPAddress> GetIPs(IEnumerable<NetworkInterface> networkInterfaces)
    {
        return networkInterfaces.SelectMany(v => v.GetIPProperties()
            .UnicastAddresses
            .Select(x => x.Address)
            .Where(x => !IPAddress.IsLoopback(x) && x.AddressFamily == AddressFamily.InterNetwork))
            .ToList();
    }

    public static List<IPAddress> GetIPs()
    {
        return GetIPs(NetworkInterface.GetAllNetworkInterfaces());
    }


    public static List<T> SmartMerge<T>(List<T> targetArr, List<T> toMerge)
    {
        List<T> missingToMerge = toMerge.Where(x => !targetArr.Contains(x)).ToList();
        List<T> newArrResult = targetArr.ToList();

        foreach(var missing in missingToMerge)
        {
            int newIndex = FindNewIndex(toMerge, newArrResult, missing);
            if (newIndex >= newArrResult.Count)
                newArrResult.Add(missing);
            else
                newArrResult.Insert(newIndex, missing);
        }

        return newArrResult;
    }

    public static int FindNewIndex<T>(List<T> originalArr, List<T> newArray, T item)
    {
        int originalIndex = originalArr.IndexOf(item);
        int newIndex = -1;

        //Search before items
        for (int i = originalIndex - 1; i >= 0; i--)
        {
            var previousItem = originalArr[i];
            var indexInNewArr = newArray.FindIndex(x => x.Equals(previousItem));
            if (indexInNewArr >= 0)
            {
                newIndex = indexInNewArr + 1;
                break;
            }
        }
        if (newIndex < 0)
            for (int i = originalIndex + 1; i < originalArr.Count; i++)
            {
                var previousItem = originalArr[i];
                var indexInNewArr = newArray.FindIndex(x => x.Equals(previousItem));
                if (indexInNewArr >= 0)
                {
                    newIndex = indexInNewArr - 1;
                    break;
                }
            }
        if (newIndex < 0)
            return originalArr.Count;
        else
            return newIndex;
    }

    public static string GenerateReadablePassword(int length)
    {
        const string validChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        byte[] randomBytes = new byte[length];
        RandomNumberGenerator.Fill(randomBytes);
        char[] result = new char[length];

        for (int i = 0; i < length; i++)
            result[i] = validChars[randomBytes[i] % validChars.Length];

        return new string(result);
    }

    public static string? FindDirectory(string directoryName)
    {
        // 1. Check next to the executable
        string? executablePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        if (executablePath != null)
        {
            string exeDirPath = Path.Combine(executablePath, directoryName);
            if (Directory.Exists(exeDirPath))
            {
                return Path.GetFullPath(exeDirPath);
            }
        }

        // 2. Check in Resources directory (macOS-specific, e.g., for bundled apps)
        if (OperatingSystem.IsMacOS() && executablePath != null)
        {
            string resourcesDirPath = Path.Combine(executablePath, "../Resources", directoryName);
            if (Directory.Exists(resourcesDirPath))
            {
                return Path.GetFullPath(resourcesDirPath);
            }
        }

        // 3. Check in the current working directory
        string workingDirPath = Path.Combine(Directory.GetCurrentDirectory(), directoryName);
        if (Directory.Exists(workingDirPath))
        {
            return Path.GetFullPath(workingDirPath);
        }

        // 4. Check in the application base directory (AppContext.BaseDirectory)
        string baseDirPath = Path.Combine(AppContext.BaseDirectory, directoryName);
        if (Directory.Exists(baseDirPath))
        {
            return Path.GetFullPath(baseDirPath);
        }

        return null;
    }
}