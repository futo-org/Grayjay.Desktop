namespace Grayjay.ClientServer.Models.Downloads
{
    public class StorageInfo
    {
        public string StorageLocation { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public long TotalBytes { get; set; }


        public static StorageInfo GetInfo(string path)
        {
            DriveInfo info = new DriveInfo(Path.GetFullPath(path));

            return new StorageInfo()
            {
                StorageLocation = path,
                TotalBytes = info.TotalSize,
                AvailableBytes = info.AvailableFreeSpace,
                UsedBytes = CalculateSize(new DirectoryInfo(path))
            };
        }

        private static long CalculateSize(DirectoryInfo info)
        {
            long total = 0;
            foreach (DirectoryInfo dir in info.GetDirectories())
                total += CalculateSize(dir);
            foreach (FileInfo file in info.GetFiles())
                total += file.Length;
            return total;
        }
    }
}
