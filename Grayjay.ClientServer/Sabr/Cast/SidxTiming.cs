using System.Buffers.Binary;
using System.Text;

namespace Grayjay.ClientServer.Sabr.Cast
{
    public class SidxTiming
    {
        private const long MAX_ANCHOR_DRIFT_US = 250_000L;

        public int Timescale { get; }
        public long[] Durations { get; }
        public long BaseTicks { get; }

        public SidxTiming(int timescale, long[] durations, long baseTicks = 0)
        {
            Timescale = timescale;
            Durations = durations;
            BaseTicks = baseTicks;
        }

        public int SegmentCount => Durations.Length;

        public int? IndexOfStartUs(long startUs)
        {
            var ticks = BaseTicks;
            var best = -1;
            var bestDelta = long.MaxValue;
            for (var i = 0; i < Durations.Length; i++)
            {
                var us = TicksToUs(ticks);
                var delta = Math.Abs(us - startUs);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
                if (us > startUs && delta > bestDelta) break;
                ticks += Durations[i];
            }
            if (best < 0) return null;

            var toleranceUs = Math.Min(MAX_ANCHOR_DRIFT_US, TicksToUs(Durations[best]) / 2);
            return bestDelta <= toleranceUs ? best : null;
        }

        private long TicksToUs(long ticks) =>
            ticks / Timescale * 1_000_000L + (ticks % Timescale) * 1_000_000L / Timescale;
    }

    public static class Mp4SidxParser
    {
        public static SidxTiming? Parse(byte[] data)
        {
            var offset = 0;
            while (offset + 8 <= data.Length)
            {
                long size32 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
                var type = Encoding.ASCII.GetString(data, offset + 4, 4);
                var boxSize = size32;
                var headerSize = 8;
                if (size32 == 1)
                {
                    if (offset + 16 > data.Length) break;
                    boxSize = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(offset + 8));
                    headerSize = 16;
                }
                else if (size32 == 0)
                    boxSize = data.Length - offset;
                if (boxSize < headerSize || boxSize > int.MaxValue) break;

                if (type == "sidx")
                    return ParseSidx(data, offset + headerSize, (int)Math.Min(offset + boxSize, data.Length));

                var next = offset + (int)boxSize;
                if (next <= offset) break;
                offset = next;
            }
            return null;
        }

        private static SidxTiming? ParseSidx(byte[] data, int start, int end)
        {
            var p = start;
            if (p + 4 > end) return null;
            var version = data[p];
            p += 4;

            p += 4;
            if (p + 4 > end) return null;
            var timescale = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
            p += 4;

            if (p + (version == 0 ? 8 : 16) > end) return null;
            long earliestPresentationTime = version == 0 ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p)) : BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(p));
            p += version == 0 ? 8 : 16;

            p += 2;
            if (p + 2 > end) return null;
            int referenceCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
            p += 2;

            if (referenceCount <= 0 || timescale <= 0) return null;
            var durations = new long[referenceCount];
            for (var i = 0; i < referenceCount; i++)
            {
                if (p + 12 > end) return null;
                durations[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p + 4));
                p += 12;
            }
            return new SidxTiming(timescale, durations, Math.Max(earliestPresentationTime, 0));
        }
    }

    public static class WebmCuesParser
    {
        private const long ID_SEGMENT = 0x18538067L;
        private const long ID_INFO = 0x1549A966L;
        private const long ID_TIMECODE_SCALE = 0x2AD7B1L;
        private const long ID_DURATION = 0x4489L;
        private const long ID_CUES = 0x1C53BB6BL;
        private const long ID_CUE_POINT = 0xBBL;
        private const long ID_CUE_TIME = 0xB3L;

        private const long NANOS_PER_SECOND = 1_000_000_000L;
        private const long DEFAULT_TIMECODE_SCALE = 1_000_000L;

        private record struct Element(long Id, int ContentStart, int ContentEnd);

        public static SidxTiming? Parse(byte[] data)
        {
            var segment = FindSegmentContent(data);
            if (segment == null) return null;

            var timecodeScaleNs = DEFAULT_TIMECODE_SCALE;
            var durationTicks = 0.0;
            var cueTimes = new List<long>();

            var pos = segment.Value.Start;
            var end = segment.Value.End;
            while (pos < end)
            {
                var el = ReadElement(data, pos, end);
                if (el == null) break;
                if (el.Value.Id == ID_INFO)
                {
                    var p = el.Value.ContentStart;
                    while (p < el.Value.ContentEnd)
                    {
                        var child = ReadElement(data, p, el.Value.ContentEnd);
                        if (child == null) break;
                        if (child.Value.Id == ID_TIMECODE_SCALE) { var v = ReadUInt(data, child.Value); if (v != null) timecodeScaleNs = v.Value; }
                        else if (child.Value.Id == ID_DURATION) { var v = ReadFloat(data, child.Value); if (v != null) durationTicks = v.Value; }
                        p = child.Value.ContentEnd;
                    }
                }
                else if (el.Value.Id == ID_CUES)
                {
                    var p = el.Value.ContentStart;
                    while (p < el.Value.ContentEnd)
                    {
                        var cuePoint = ReadElement(data, p, el.Value.ContentEnd);
                        if (cuePoint == null) break;
                        if (cuePoint.Value.Id == ID_CUE_POINT)
                        {
                            var q = cuePoint.Value.ContentStart;
                            while (q < cuePoint.Value.ContentEnd)
                            {
                                var child = ReadElement(data, q, cuePoint.Value.ContentEnd);
                                if (child == null) break;
                                if (child.Value.Id == ID_CUE_TIME)
                                {
                                    var v = ReadUInt(data, child.Value);
                                    if (v != null) cueTimes.Add(v.Value);
                                    break;
                                }
                                q = child.Value.ContentEnd;
                            }
                        }
                        p = cuePoint.Value.ContentEnd;
                    }
                }
                pos = el.Value.ContentEnd;
            }

            if (timecodeScaleNs <= 0) return null;
            if (NANOS_PER_SECOND % timecodeScaleNs != 0) return null;
            var timescale = (int)(NANOS_PER_SECOND / timecodeScaleNs);
            if (timescale <= 0) return null;

            var times = cueTimes.Distinct().OrderBy(x => x).ToList();
            if (times.Count < 2) return null;

            var durations = new long[times.Count];
            for (var i = 0; i < times.Count - 1; i++)
                durations[i] = Math.Max(times[i + 1] - times[i], 1);

            var lastStart = times[^1];
            var tail = (long)durationTicks - lastStart;
            durations[^1] = (durationTicks > 0 && tail > 0) ? tail : (durations.Length >= 2 ? durations[^2] : 1L);

            return new SidxTiming(timescale, durations, times[0]);
        }

        private static (int Start, int End)? FindSegmentContent(byte[] data)
        {
            var pos = 0;
            while (pos < data.Length)
            {
                var el = ReadElement(data, pos, data.Length);
                if (el == null) return null;
                if (el.Value.Id == ID_SEGMENT) return (el.Value.ContentStart, el.Value.ContentEnd);
                pos = el.Value.ContentEnd;
            }
            return null;
        }

        private static Element? ReadElement(byte[] data, int pos, int limit)
        {
            if (pos < 0 || pos >= limit) return null;

            var idLen = VintLength(data[pos]);
            if (idLen == 0 || pos + idLen > limit) return null;
            long id = 0;
            for (var i = 0; i < idLen; i++) id = (id << 8) | data[pos + i];

            var sizePos = pos + idLen;
            if (sizePos >= limit) return null;
            var sizeLen = VintLength(data[sizePos]);
            if (sizeLen == 0 || sizePos + sizeLen > limit) return null;

            long size = data[sizePos] & (0xFF >> sizeLen);
            var unknown = size == (0xFF >> sizeLen);
            for (var i = 1; i < sizeLen; i++)
            {
                var b = data[sizePos + i];
                if (b != 0xFF) unknown = false;
                size = (size << 8) | b;
            }

            var contentStart = sizePos + sizeLen;
            int contentEnd;
            if (unknown) contentEnd = limit;
            else
            {
                var e = contentStart + size;
                contentEnd = (e < contentStart || e > limit) ? limit : (int)e;
            }
            return new Element(id, contentStart, contentEnd);
        }

        private static int VintLength(byte b)
        {
            if (b == 0) return 0;
            var mask = 0x80;
            for (var len = 1; len <= 8; len++)
            {
                if ((b & mask) != 0) return len;
                mask >>= 1;
            }
            return 0;
        }

        private static long? ReadUInt(byte[] data, Element el)
        {
            var len = el.ContentEnd - el.ContentStart;
            if (len <= 0 || len > 8) return null;
            long v = 0;
            for (var i = 0; i < len; i++) v = (v << 8) | data[el.ContentStart + i];
            return v;
        }

        private static double? ReadFloat(byte[] data, Element el)
        {
            var len = el.ContentEnd - el.ContentStart;
            if (len == 4) return BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(el.ContentStart));
            if (len == 8) return BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(el.ContentStart));
            return null;
        }
    }
}
