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

        var message = id;
        var body = Encoding.UTF8.GetBytes(message);
        _rabbitChannel.BasicPublish(exchange: "",
                                    routingKey: "rank_tasks",
                                    basicProperties: null,
                                    body: body);

        return Redirect($"summary?id={id}");
    }

     private double CalculateRank(string text)
    {
        int letters = Regex.Matches(text, @"[а-яА-Яa-zA-ZёЁ]").Count;
        return (double)letters / text.Length;
    }
}
