namespace BookingService.Shard1
{
    public interface IShardResolver
    {
        Task<int> ResolveShardForShowAsync(long showId, CancellationToken ct = default);
    }
}
