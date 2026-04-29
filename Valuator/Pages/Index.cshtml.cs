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
    private readonly IDatabase _redisDb;
    private readonly IConnectionMultiplexer _redis;
    private readonly IModel _rabbitChannel;

    public IndexModel(ILogger<IndexModel> logger, IConnectionMultiplexer redis, IModel rabbitChannel)
    {
      _logger = logger;
      _redis = redis;
      _redisDb = redis.GetDatabase();
      _rabbitChannel = rabbitChannel;
    }

    public void OnGet()
    {

    }

    public IActionResult OnPost(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ModelState.AddModelError(string.Empty, "Текст не может быть пустым или состоять только из пробелов.");
            return Page();
        }

        _logger.LogDebug(text);

        string id = Guid.NewGuid().ToString();
        _redisDb.StringSet($"TEXT-{id}", text);
        double similarity = CalculateSimilarity(text, id);
        _redisDb.StringSet($"SIMILARITY-{id}", similarity.ToString());

        PublishSimilarityEvent(id, similarity);
        PublishRankTask(id);

        return Redirect($"summary?id={id}");
    }

     private double CalculateSimilarity(string text, string currentId)
    {
        var endpoints = _redis.GetEndPoints();
        if (endpoints.Length == 0)
        {
            _logger.LogWarning("No Redis endpoints available");
            return 0.0;
        }

        var server = _redis.GetServer(endpoints[0]);
        var keys = server.Keys(pattern: "TEXT-*");

        foreach (var key in keys)
        {
            if (key.ToString() != $"TEXT-{currentId}")
            {
                var savedText = _redisDb.StringGet(key);
                if (savedText == text)
                {
                    return 1.0;
                }
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
