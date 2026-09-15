using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly NotificationService _notify;
    private readonly UserManager<ApplicationUser> _users;
    public PublicApiController(SawmDbContext db, ApiKeyService keys, NotificationService notify, UserManager<ApplicationUser> users)
    { _db = db; _keys = keys; _notify = notify; _users = users; }

    private const int MaxRows = 500;

    /// <summary>يُشعر الأدمن (جرس + توست + بريد الموجز) بحدث شحن من نظام خارجي.</summary>
    private async Task NotifyAdminsAsync(string title, string body)
    {
        try
        {
            var admins = await _users.GetUsersInRoleAsync(Roles.Admin);
            var ids = admins.Select(a => a.Id).ToList();
            if (ids.Count > 0)
                await _notify.PushManyAsync(ids, title, body, "/Shipping");
        }
        catch { /* الإشعار مكمّل — لا يُعطّل تحديث الشحنة */ }
    }

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

    [HttpGet("shipments")]
    public async Task<IActionResult> Shipments()
    {
        var (fields, error) = await AuthorizeResource("shipments");
        if (error is not null) return error;
        // فقط الطلبات التي أفرج عنها الأدمن لأنظمة الشحن
        var rows = await _db.Contracts.AsNoTracking()
            .Include(c => c.Crop).Include(c => c.Auction)
            .Where(c => c.ShippingReleased)
            .OrderByDescending(c => c.ShippingReleasedAt).Take(MaxRows).ToListAsync();
        var data = rows.Select(c => ApiCatalog.Filter(ApiCatalog.MapShipment(c), fields!));
        return Payload("shipments", fields!, data);
    }

    /// <summary>
    /// يستدعيها نظام اللوجستيك عند "قبول استلام الشحنة" فتتحدّث الحالة في ساوم.
    /// يتطلب مفتاحاً يملك صلاحية مورد shipments. (كتابة محصورة: تأكيد الاستلام فقط.)
    /// </summary>
    [HttpPost("shipments/{id:int}/receive")]
    public async Task<IActionResult> ReceiveShipment(int id, [FromBody] ReceiveShipmentDto? body = null)
    {
        var (key, _, error) = await AuthorizeKey("shipments");
        if (error is not null) return error;

        var c = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id && x.ShippingReleased);
        if (c is null)
            return NotFound(new { error = "not_found", message = "لا توجد شحنة مُرسَلة بهذا المعرّف." });

        if (!c.ShippingReceived)
        {
            c.ShippingReceived = true;
            c.ShippingReceivedAt = DateTime.Now;
            var system = string.IsNullOrWhiteSpace(body?.System) ? key!.Name : body!.System!.Trim();
            c.ShippingReceivedBy = system.Length > 120 ? system[..120] : system;
            c.ShipmentStage = ShipmentStage.Received;
            c.ShipmentStageAt = DateTime.Now;
            await _db.SaveChangesAsync();
            await NotifyAdminsAsync("تم استلام شحنة",
                $"استلم «{c.ShippingReceivedBy}» الشحنة للعقد {c.ContractNumber}.");
        }

        return Ok(new
        {
            id = c.Id,
            contractNumber = c.ContractNumber,
            received = true,
            receivedAt = c.ShippingReceivedAt,
            receivedBy = c.ShippingReceivedBy,
            stage = ApiCatalog.StageCode(c.ShipmentStage),
            message = "تم تأكيد استلام الشحنة من منصة ساوم."
        });
    }

    /// <summary>
    /// يحدّث نظام اللوجستيك مرحلة الشحن: received | in_transit | delivered.
    /// يتطلب مفتاحاً يملك صلاحية مورد shipments.
    /// </summary>
    [HttpPost("shipments/{id:int}/status")]
    public async Task<IActionResult> UpdateShipmentStage(int id, [FromBody] ShipmentStageDto? body = null)
    {
        var (key, _, error) = await AuthorizeKey("shipments");
        if (error is not null) return error;

        var stage = ApiCatalog.ParseStage(body?.Stage);
        if (stage is null)
            return BadRequest(new { error = "bad_request", message = "قيمة stage غير صحيحة. المسموح: received | in_transit | delivered." });

        var c = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id && x.ShippingReleased);
        if (c is null)
            return NotFound(new { error = "not_found", message = "لا توجد شحنة مُرسَلة بهذا المعرّف." });

        c.ShipmentStage = stage.Value;
        c.ShipmentStageAt = DateTime.Now;
        if (stage.Value >= ShipmentStage.Received && !c.ShippingReceived)
        {
            c.ShippingReceived = true;
            c.ShippingReceivedAt = DateTime.Now;
            c.ShippingReceivedBy = string.IsNullOrWhiteSpace(body?.System) ? key!.Name : body!.System!.Trim();
        }
        await _db.SaveChangesAsync();

        var stageAr = stage.Value switch
        {
            ShipmentStage.Received => "استُلمت",
            ShipmentStage.InTransit => "قيد النقل",
            ShipmentStage.Delivered => "سُلّمت",
            _ => "محدّثة"
        };
        await NotifyAdminsAsync("تحديث حالة شحنة",
            $"الشحنة للعقد {c.ContractNumber}: {stageAr} — بواسطة «{c.ShippingReceivedBy ?? key!.Name}».");

        return Ok(new
        {
            id = c.Id,
            contractNumber = c.ContractNumber,
            stage = ApiCatalog.StageCode(c.ShipmentStage),
            stageAt = c.ShipmentStageAt,
            message = "تم تحديث مرحلة الشحن."
        });
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

    private string? ReadProvidedKey()
    {
        var provided = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            var auth = Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = auth["Bearer ".Length..].Trim();
        }
        return provided;
    }

    /// <summary>يصادق على المفتاح ويتحقق أن المورد مسموح له، ويعيد المفتاح والحقول المسموح بها.</summary>
    private async Task<(ApiKey? key, string[]? fields, IActionResult? error)> AuthorizeKey(string resource)
    {
        var key = await _keys.AuthenticateAsync(ReadProvidedKey());
        if (key is null)
            return (null, null, Unauthorized(new { error = "unauthorized", message = "مفتاح API غير صالح أو غير مفعّل." }));

        var map = ApiCatalog.ParseFields(key.FieldsJson);
        if (!map.TryGetValue(resource, out var fields) || fields.Length == 0)
            return (null, null, StatusCode(StatusCodes.Status403Forbidden,
                new { error = "forbidden", message = $"هذا المفتاح لا يملك صلاحية الوصول إلى '{resource}'." }));

        return (key, fields, null);
    }

    /// <summary>نسخة للقراءة: تُهمل المفتاح وتعيد الحقول والخطأ فقط.</summary>
    private async Task<(string[]? fields, IActionResult? error)> AuthorizeResource(string resource)
    {
        var (_, fields, error) = await AuthorizeKey(resource);
        return (fields, error);
    }
}

/// <summary>جسم طلب تأكيد استلام الشحنة (اختياري) من نظام اللوجستيك.</summary>
public record ReceiveShipmentDto(string? System, string? Reference);

/// <summary>جسم طلب تحديث مرحلة الشحن من نظام اللوجستيك.</summary>
public record ShipmentStageDto(string? Stage, string? System, string? Note);
