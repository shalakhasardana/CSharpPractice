
using Npgsql;

namespace BookingService.Shard1
{
    public class CatalogFirstShardResolver : IShardResolver
    {
        private readonly NpgsqlDataSource _catalog;
        private readonly int _fallbackShards;

        public CatalogFirstShardResolver(NpgsqlDataSource catalog, int fallbackShards)
        {
            _catalog = catalog;
            _fallbackShards = fallbackShards;
        }

        public async Task<int> ResolveShardForShowAsync(long showId, CancellationToken ct = default)
        {
            await using var cmd = _catalog.CreateCommand(
        "SELECT shard_id FROM catalog.show_shard_map WHERE show_id=@sid");
            cmd.Parameters.AddWithValue("sid", showId);

            var o = await cmd.ExecuteScalarAsync(ct);
            if (o is int shardId) return shardId;

            if (_fallbackShards > 0) return JumpHash.Hash(showId, _fallbackShards);

            throw new InvalidOperationException($"No shard mapping for show {showId}");
        }
    }
}
