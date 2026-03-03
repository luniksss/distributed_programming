using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StackExchange.Redis;
using System.Text.RegularExpressions;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IDatabase _redisDb;
    private readonly IConnectionMultiplexer _redis;

    public IndexModel(ILogger<IndexModel> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
        _redisDb = redis.GetDatabase();
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

        string textKey = "TEXT-" + id;
        // TODO: (pa1) сохранить в БД (Redis) text по ключу textKey
        _redisDb.StringSet(textKey, text);

        string rankKey = "RANK-" + id;
        // TODO: (pa1) посчитать rank и сохранить в БД (Redis) по ключу rankKey
        double rank = CalculateRank(text);
        _redisDb.StringSet(rankKey, rank.ToString());

        string similarityKey = "SIMILARITY-" + id;
        // TODO: (pa1) посчитать similarity и сохранить в БД (Redis) по ключу similarityKey
        double similarity = CalculateSimilarity(text, id);
        _redisDb.StringSet(similarityKey, similarity.ToString());

        return Redirect($"summary?id={id}");
    }

     private double CalculateRank(string text)
    {
        int letters = Regex.Matches(text, @"[а-яА-Яa-zA-ZёЁ]").Count;
        return (double)letters / text.Length;
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
}
