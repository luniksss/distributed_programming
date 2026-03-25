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
    public bool IsRankComputed { get; set; }

    public void OnGet(string id)
    {
        _logger.LogDebug(id);

        string rankKey = $"RANK-{id}";
        var rankValue = _redisDb.StringGet(rankKey);
        _logger.LogDebug($"Rank value: {rankValue}");
        if (double.TryParse(rankValue, out double rank))
        {
            Rank = rank;
            IsRankComputed = true;
            _logger.LogDebug($"Rank parsed: {rank}");
        }
        else
        {
            IsRankComputed = false;
            _logger.LogDebug("Rank not found or invalid");
        }

        string similarityKey = $"SIMILARITY-{id}";
        var similarityValue = _redisDb.StringGet(similarityKey);
        if (double.TryParse(similarityValue, out double similarity))
        {
            Similarity = similarity;
            _logger.LogDebug($"Similarity: {similarity}");
        }
    }
}