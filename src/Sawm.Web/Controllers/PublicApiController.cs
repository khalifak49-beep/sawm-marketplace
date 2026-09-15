using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>
/// واجهة القراءة العمومية للأنظمة الخارجية. المصادقة عبر ترويسة X-API-Key
/// (أو Authorization: Bearer). تُعاد الحقول المسموح بها لكل مفتاح فقط.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1")]
[Produces("application/json")]
public class PublicApiController : ControllerBase
{
    private readonly SawmDbContext _db;
    private readonly ApiKeyService _keys;
    public PublicApiController(SawmDbContext db, ApiKeyService keys) { _db = db; _keys = keys; }

    private const int MaxRows = 500;

    /// <summary>دليل الموارد والحقول المتاحة (لا يتطلب مفتاحاً) — يساعد النظام الآخر على التكامل.</summary>
    [HttpGet("")]
    [HttpGet("meta")]
    public IActionResult Meta() => Ok(new
    {
        service = "Sawm Marketplace API",
        version = "v1",
        auth = "أرسل ترويسة: X-API-Key: <your key>  (أو Authorization: Bearer <your key>)",
        resources = ApiCatalog.Resources.Select(r => new
        {
            r.Key,
            r.Label,
            r.Endpoint,
            fields = r.Fields.Select(f => new { f.Key, f.Label })
        })
    });

    [HttpGet("auctions")]
    public async Task<IActionResult> Auctions()
    {
        var (fields, error) = await AuthorizeResource("auctions");
        if (error is not null) return error;
        var rows = await _db.Auctions.AsNoTracking()
            .Include(a => a.Crop).Include(a => a.Bids)
            .OrderByDescending(a => a.CreatedAt).Take(MaxRows).ToListAsync();
        var data = rows.Select(a => ApiCatalog.Filter(ApiCatalog.MapAuction(a), fields!));
        return Payload("auctions", fields!, data);
    }

    [HttpGet("tenders")]
    public async Task<IActionResult> Tenders()
    {
        var (fields, error) = await AuthorizeResource("tenders");
        if (error is not null) return error;
        var rows = await _db.Tenders.AsNoTracking()
            .Include(t => t.Crop).Include(t => t.Offers)
            .OrderByDescending(t => t.CreatedAt).Take(MaxRows).ToListAsync();
        var data = rows.Select(t => ApiCatalog.Filter(ApiCatalog.MapTender(t), fields!));
        return Payload("tenders", fields!, data);
    }

    [HttpGet("contracts")]
    public async Task<IActionResult> Contracts()
    {
        var (fields, error) = await AuthorizeResource("contracts");
        if (error is not null) return error;
        var rows = await _db.Contracts.AsNoTracking()
            .Include(c => c.Crop)
            .OrderByDescending(c => c.CreatedAt).Take(MaxRows).ToListAsync();
        var data = rows.Select(c => ApiCatalog.Filter(ApiCatalog.MapContract(c), fields!));
        return Payload("contracts", fields!, data);
    }

    [HttpGet("crops")]
    public async Task<IActionResult> Crops()
    {
        var (fields, error) = await AuthorizeResource("crops");
        if (error is not null) return error;
        var rows = await _db.Crops.AsNoTracking()
            .OrderBy(c => c.Name).Take(MaxRows).ToListAsync();
        var data = rows.Select(c => ApiCatalog.Filter(ApiCatalog.MapCrop(c), fields!));
        return Payload("crops", fields!, data);
    }

    // ── مساعدات ──

    private IActionResult Payload(string resource, string[] fields, IEnumerable<Dictionary<string, object?>> data)
    {
        var list = data.ToList();
        return Ok(new { resource, count = list.Count, fields, data = list });
    }

    /// <summary>يصادق على المفتاح ويتحقق أن المورد مسموح له، ويعيد الحقول المسموح بها.</summary>
    private async Task<(string[]? fields, IActionResult? error)> AuthorizeResource(string resource)
    {
        var provided = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            var auth = Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = auth["Bearer ".Length..].Trim();
        }

        var key = await _keys.AuthenticateAsync(provided);
        if (key is null)
            return (null, Unauthorized(new { error = "unauthorized", message = "مفتاح API غير صالح أو غير مفعّل." }));

        var map = ApiCatalog.ParseFields(key.FieldsJson);
        if (!map.TryGetValue(resource, out var fields) || fields.Length == 0)
            return (null, StatusCode(StatusCodes.Status403Forbidden,
                new { error = "forbidden", message = $"هذا المفتاح لا يملك صلاحية الوصول إلى '{resource}'." }));

        return (fields, null);
    }
}
