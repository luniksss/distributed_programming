using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.AspNetCore.Identity;

namespace Valuator.Pages;
public class SummaryModel : PageModel
{
    private readonly ILogger<SummaryModel> _logger;
    private readonly IDatabase _redisDb;
    private readonly UserManager<IdentityUser> _userManager;

    public SummaryModel(ILogger<SummaryModel> logger, IConnectionMultiplexer redis, UserManager<IdentityUser> userManager)
    {
        _logger = logger;
        _redisDb = redis.GetDatabase();
        _userManager = userManager;
    }

    public double Rank { get; set; }
    public double Similarity { get; set; }
    public bool IsRankComputed { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
    {
        _logger.LogDebug(id);

        if (!User.Identity.IsAuthenticated)
            return RedirectToPage("/Account/Login");

        var userId = await _redisDb.HashGetAsync($"TEXT-{id}", "UserId");
        var currentUserId = _userManager.GetUserId(User);

        if (userId.IsNullOrEmpty || userId != currentUserId)
            return RedirectToPage("/Error");

        string rankKey = $"RANK-{id}";
        var rankValue = await _redisDb.GetAsync(rankKey);
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
        var similarityValue = await _redisDb.StringGetAsync(similarityKey);
        if (double.TryParse(similarityValue, out double similarity))
        {
            Similarity = similarity;
            _logger.LogDebug($"Similarity: {similarity}");
        }

        return Page();
    }
}