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
    private readonly IShardResolver _shardResolver;

    public SummaryModel(ILogger<SummaryModel> logger, IShardResolver shardResolver)
    {
        _logger = logger;
        _shardResolver = shardResolver;
    }

    public double Rank { get; set; }
    public double Similarity { get; set; }
    public bool IsRankComputed { get; set; }

    public void OnGet(string id)
    {        
        string region = _shardResolver.GetShardKey(id);
        if (string.IsNullOrEmpty(region))
        {
            _logger.LogWarning("регион для ID {Id} не найден", id);
            IsRankComputed = false;
            return;
        }

        _logger.LogInformation("LOOKUP: {Id}, {Region}", id, region);
        IDatabase shardDb = _shardResolver.GetShardDatabase(region);
        
        string rankKey = $"RANK-{id}";
        var rankValue = shardDb.StringGet(rankKey);
        _logger.LogDebug($"rank: {rankValue}");
        if (double.TryParse(rankValue, out double rank))
        {
            Rank = rank;
            IsRankComputed = true;
            _logger.LogDebug($"rank распарсенный: {rank}");
        }
        else
        {
            IsRankComputed = false;
            _logger.LogDebug("rank не найден или невалидный");
        }

        string similarityKey = $"SIMILARITY-{id}";
        var similarityValue = shardDb.StringGet(similarityKey);
        if (double.TryParse(similarityValue, out double similarity))
        {
            Similarity = similarity;
            _logger.LogDebug($"similarity: {similarity}");
        }
    }
}