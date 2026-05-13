using StackExchange.Redis;

public interface IShardResolver
{
    string GetShardKey(string textId);
    IDatabase GetShardDatabase(string shardKey);
    void SaveShardMapping(string textId, string shardKey);
}

public class ShardResolver : IShardResolver
{
    private readonly IConnectionMultiplexer _mainRedis;
    private readonly Dictionary<string, IConnectionMultiplexer> _shardConnections;

    public ShardResolver(
        [FromKeyedServices("MAIN")] IConnectionMultiplexer mainRedis,
        [FromKeyedServices("RU")] IConnectionMultiplexer ruRedis,
        [FromKeyedServices("EU")] IConnectionMultiplexer euRedis,
        [FromKeyedServices("ASIA")] IConnectionMultiplexer asiaRedis)
    {
        _mainRedis = mainRedis;
        _shardConnections = new Dictionary<string, IConnectionMultiplexer>
        {
            ["RU"] = ruRedis,
            ["EU"] = euRedis,
            ["ASIA"] = asiaRedis
        };
    }

    public string GetShardKey(string textId)
    {
        var db = _mainRedis.GetDatabase();
        string key = $"SHARD-{textId}";
        return db.StringGet(key);
    }

    public IDatabase GetShardDatabase(string shardKey)
    {
        if (_shardConnections.TryGetValue(shardKey, out var conn))
            return conn.GetDatabase();
        throw new ArgumentException($"Uknown instance: {shardKey}");
    }

    public void SaveShardMapping(string textId, string shardKey)
    {
        var db = _mainRedis.GetDatabase();
        string key = $"SHARD-{textId}";
        db.StringSet(key, shardKey);
    }
}