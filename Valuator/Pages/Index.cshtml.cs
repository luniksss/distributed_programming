using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;
using System.Text.RegularExpressions;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IShardResolver _shardResolver;
    private readonly IModel _rabbitChannel;

    private readonly Dictionary<string, string> _countryToRegion = new()
    {
        ["Russia"] = "RU",
        ["France"] = "EU",
        ["Germany"] = "EU",
        ["UAE"] = "ASIA",
        ["India"] = "ASIA"
    };

    public IndexModel(ILogger<IndexModel> logger, IShardResolver shardResolver, IModel rabbitChannel)
    {
      _logger = logger;
      _shardResolver = shardResolver;
      _rabbitChannel = rabbitChannel;
    }

    public void OnGet()
    {

    }

    public IActionResult OnPost(string text, string country)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ModelState.AddModelError(string.Empty, "Текст не может быть пустым или состоять только из пробелов.");
            return Page();
        }

        _logger.LogDebug(text);
        if (string.IsNullOrEmpty(country) || !_countryToRegion.ContainsKey(country))
        {
            ModelState.AddModelError(string.Empty, "Выберите корректную страну.");
            return Page();
        }

        string region = _countryToRegion[country];
        string id = Guid.NewGuid().ToString();

        _shardResolver.SaveShardMapping(id, region);
        IDatabase shardDb = _shardResolver.GetShardDatabase(region);
        shardDb.StringSet($"TEXT-{id}", text);

        double similarity = CalculateSimilarityInShard(text, id, shardDb, region);
        shardDb.StringSet($"SIMILARITY-{id}", similarity.ToString());

        PublishSimilarityEvent(id, similarity);
        PublishRankTask(id);

        return Redirect($"summary?id={id}");
    }

    private double CalculateSimilarityInShard(string text, string currentId, IDatabase shardDb, string region)
    {
        _logger.LogInformation("LOOKUP: {Id}, {Region}", currentId, region);
        var endpoints = ((IConnectionMultiplexer?)shardDb.Multiplexer)?.GetEndPoints();
        if (endpoints == null || endpoints.Length == 0)
            return 0.0;

        var server = shardDb.Multiplexer.GetServer(endpoints[0]);
        var keys = server.Keys(pattern: "TEXT-*");

        foreach (var key in keys)
        {
            if (key.ToString() != $"TEXT-{currentId}")
            {
                var savedText = shardDb.StringGet(key);
                if (savedText == text)
                    return 1.0;
            }
        }
        return 0.0;
    }

    private void PublishSimilarityEvent(string id, double similarity)
    {
        var similarityEvent = JsonSerializer.Serialize(new
        {
            Type = "SimilarityCalculated",
            Id = id,
            Value = similarity
        });
        _rabbitChannel.BasicPublish(
            exchange: "events_exchange",
            routingKey: "",
            body: Encoding.UTF8.GetBytes(similarityEvent)
        );
    }

    private void PublishRankTask(string id)
    {
        _rabbitChannel.BasicPublish(
            exchange: "",
            routingKey: "rank_tasks",
            body: Encoding.UTF8.GetBytes(id)
        );
    }
}
