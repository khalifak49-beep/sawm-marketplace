using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>
/// طلبات الشحن: العقود الموقّعة الجاهزة للشحن. يُفرج الأدمن عن الطلب فيظهر
/// لأنظمة الشحن الخارجية عبر مورد الـAPI /api/v1/shipments (قراءة، بحقول محددة).
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class ShippingController : Controller
{
    private readonly SawmDbContext _db;
    public ShippingController(SawmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // الجاهز للشحن: موقّع من الطرفين وحالته نشط أو جاهز للتسليم
        var rows = await _db.Contracts.AsNoTracking()
            .Include(c => c.Crop)
            .Include(c => c.Seller)
            .Include(c => c.Buyer)
            .Include(c => c.Auction)
            .Where(c => c.SellerSigned && c.BuyerSigned
                     && (c.Status == ContractStatus.Active || c.Status == ContractStatus.ReadyForDelivery))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(rows);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(int id, decimal commissionRate)
    {
        var c = await _db.Contracts.FindAsync(id);
        if (c is not null && !c.ShippingReleased)
        {
            c.ShippingCommissionRate = commissionRate < 0 ? 0 : (commissionRate > 100 ? 100 : commissionRate);
            c.ShippingReleased = true;
            c.ShippingReleasedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"تم إرسال طلب الشحن للعقد {c.ContractNumber} (عمولة ساوم {c.ShippingCommissionRate:0.##}%) إلى أنظمة الشحن المرتبطة.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Recall(int id)
    {
        var c = await _db.Contracts.FindAsync(id);
        if (c is not null && c.ShippingReleased)
        {
            c.ShippingReleased = false;
            c.ShippingReleasedAt = null;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"تم سحب طلب الشحن للعقد {c.ContractNumber} من أنظمة الشحن.";
        }
        return RedirectToAction(nameof(Index));
    }
}
