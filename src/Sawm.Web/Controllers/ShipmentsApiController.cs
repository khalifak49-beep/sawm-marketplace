using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;

namespace Sawm.Web.Controllers;

/// <summary>
/// واجهة برمجية (API) تعرضها منصة ساوم لمنصة الشحن اللوجستي.
/// تُرجع "الشحنات المراد شحنها" (عقود جاهزة/نشطة تحتاج نقلاً) — بيانات المسار والحمولة فقط،
/// دون كشف هوية البائع أو المشتري (حفاظاً على سرّية التداول). الاعتماد الحقيقي وموافقات
/// الوسيط/الإدارة تُضاف في المرحلة القادمة؛ هذه نسخة عرض تعمل ببيانات ساوم الفعلية.
/// </summary>
[ApiController]
[Route("api/shipments")]
[AllowAnonymous]
public class ShipmentsApiController : ControllerBase
{
    private readonly SawmDbContext _db;
    public ShipmentsApiController(SawmDbContext db) => _db = db;

    // مفتاح عرض تجريبي — يُستبدل بمصادقة حقيقية عند الفصل الفعلي
    private const string DemoApiKey = "SAWM-LOGISTICS-DEMO";

    [HttpGet]
    public async Task<IActionResult> Get([FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        // إن أُرسل مفتاح فيجب أن يطابق — وإلا يُقبل الطلب في وضع العرض
        if (!string.IsNullOrEmpty(apiKey) && apiKey != DemoApiKey)
            return Unauthorized(new { error = "مفتاح API غير صالح." });

        var rows = await _db.Contracts.AsNoTracking()
            .Include(c => c.Crop).Include(c => c.Seller).Include(c => c.Buyer)
            .Where(c => c.Status == ContractStatus.Active || c.Status == ContractStatus.ReadyForDelivery)
            .OrderBy(c => c.DeliveryDate)
            .ToListAsync();

        var shipments = rows.Select(c => new
        {
            reference = c.ContractNumber,
            crop = c.Crop?.Name ?? "—",
            quantity = c.Quantity,
            unit = "طن",
            pickup = c.Seller?.Region ?? "—",
            destination = c.DeliveryLocation ?? c.Buyer?.Region ?? "—",
            readyDate = c.DeliveryDate.ToString("yyyy-MM-dd"),
            coldChain = c.Crop?.Category is "خضروات" or "فواكه",
            status = c.Status == ContractStatus.ReadyForDelivery ? "جاهزة للنقل" : "قيد التجهيز"
            // ملاحظة: لا نكشف اسم البائع/المشتري — فقط المسار والحمولة
        }).ToList();

        return Ok(new
        {
            source = "منصة ساوم",
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            count = shipments.Count,
            shipments
        });
    }
}
