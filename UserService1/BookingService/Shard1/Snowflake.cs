namespace BookingService.Shard1
{
    public class Snowflake
    {
        private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly int _shardId;
        private long _lastTs = -1;
        private long _seq = 0;
        private readonly object _lock = new();

        public Snowflake(int shardId) => _shardId = shardId & 0x3FF;

        public long NextId()
        {
            lock (_lock)
            {
                var ts = (long)(DateTimeOffset.UtcNow - Epoch).TotalMilliseconds;
                if (ts == _lastTs)
                {
                    _seq = (_seq + 1) & 0xFFF;
                    if (_seq == 0)
                        while ((long)(DateTimeOffset.UtcNow - Epoch).TotalMilliseconds == ts) { }
                }
                else
                {
                    _seq = 0;
                    _lastTs = ts;
                }
                return (ts << 22) | ((long)_shardId << 12) | _seq;
            }
        }
    }
}
