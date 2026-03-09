using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/public/plans")]
public class PublicPlansController(ControlDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.BillingPlans.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(ct);
        return Ok(rows.Select(p => new
        {
            p.Code,
            p.Name,
            p.PricingModel,
            p.PriceMonthly,
            p.PriceYearly,
            p.TaxMode,
            p.UsageUnitName,
            p.IncludedQuantity,
            p.Currency,
            Features = ParseStringList(p.FeaturesJson),
            Limits = ParseLimits(p.LimitsJson)
        }));
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; }
    }
    private static Dictionary<string, int> ParseLimits(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new(); } catch { return new(); }
    }
}
