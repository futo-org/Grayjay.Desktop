namespace Grayjay.ClientServer.Models.Downloads
{
    public class SpeedMonitor
    {
        TimeSpan _windowSize;
        DateTime _lastUpdate = DateTime.Now;
        long _readSince = 0;
        long _lastSpeed = 0;


        public SpeedMonitor(TimeSpan windowSize = default(TimeSpan))
        {
            if (windowSize.TotalSeconds == 0)
                windowSize = TimeSpan.FromMilliseconds(500);
            _windowSize = windowSize;
        }

        public long Activity(long value)
        {
            _readSince += value;

            DateTime now = DateTime.Now;
            TimeSpan diff = DateTime.Now.Subtract(_lastUpdate);
            long currentSpeed = (long)(_readSince / diff.TotalSeconds);
            if (diff > _windowSize)
            {
                _lastSpeed = currentSpeed;
                _readSince = 0;
                _lastUpdate = DateTime.Now;
            }
            return currentSpeed;
        }

        public long GetCurrentSpeed()
        {
            return _lastSpeed;
        }
    }
}
