namespace BookingService.Shard1
{
    public static class JumpHash
    {
        public static int Hash(long key, int numBuckets)
        {
            long b = -1, j = 0;
            while (j < numBuckets)
            {
                b = j;
                key = key * 2862933555777941757L + 1;
                j = (long)Math.Floor((b + 1) * ((1L << 31) / (double)((key >> 33) + 1)));
            }
            return (int)b;
        }
    }
}
