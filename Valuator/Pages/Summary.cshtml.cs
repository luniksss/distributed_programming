using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Valuator.Pages;
public class SummaryModel : PageModel
{
    private readonly ILogger<SummaryModel> _logger;
    private readonly IDatabase _redisDb;

    public SummaryModel(ILogger<SummaryModel> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redisDb = redis.GetDatabase();
    }

    public double Rank { get; set; }
    public double Similarity { get; set; }

    public void OnGet(string id)
    {
        _logger.LogDebug(id);

        // TODO: (pa1) проинициализировать свойства Rank и Similarity значениями из БД (Redis)
        string rankKey = "RANK-" + id;
        var rankValue = _redisDb.StringGet(rankKey);
        if (double.TryParse(rankValue, out double rank))
        {
            Rank = rank;
        }

        // ключи в константы
        string similarityKey = "SIMILARITY-" + id;
        var similarityValue = _redisDb.StringGet(similarityKey);
        if (double.TryParse(similarityValue, out double similarity))
        {
            Similarity = similarity;
        }
    }
}
