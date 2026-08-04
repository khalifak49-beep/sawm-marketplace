using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>
/// سوق لوجستي مستقل داخل المنصة. المنصة تربط وتوثّق فقط،
/// ولا تتحمل أي مسؤولية عن النقل أو التخزين أو التأمين.
/// </summary>
[Authorize]
public class LogisticsController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly NotificationService _notify;

    public LogisticsController(SawmDbContext db, UserManager<ApplicationUser> users, NotificationService notify)
    {
        _db = db;
        _users = users;
        _notify = notify;
    }

    private string Uid => _users.GetUserId(User)!;

    public async Task<IActionResult> Index(bool mine = false)
    {
        var uid = Uid;
        var query = _db.LogisticsRequests.AsNoTracking()
            .Include(r => r.Requester).Include(r => r.Offers).Include(r => r.Contract)
            .AsQueryable();

        if (mine)
            query = query.Where(r => r.RequesterId == uid || r.Offers.Any(o => o.ProviderId == uid));

        ViewBag.Mine = mine;
        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? contractId)
    {
        var model = new LogisticsRequest { PickupDate = DateTime.Now.AddDays(3) };

        if (contractId is int cid)
        {
            var contract = await _db.Contracts.AsNoTracking()
                .Include(c => c.Crop)
                .FirstOrDefaultAsync(c => c.Id == cid && (c.SellerId == Uid || c.BuyerId == Uid || c.BrokerId == Uid));

            if (contract is not null)
            {
                model.ContractId = contract.Id;
                model.ToLocation = contract.DeliveryLocation ?? string.Empty;
                model.WeightKg = contract.Quantity;
                model.PickupDate = contract.DeliveryDate.AddDays(-1);
                ViewBag.ContractNumber = contract.ContractNumber;
            }
        }

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LogisticsRequest model)
    {
        ModelState.Remove(nameof(LogisticsRequest.RequesterId));

        if (!ModelState.IsValid) return View(model);

        model.RequesterId = Uid;
        model.Status = LogisticsRequestStatus.Open;
        model.CreatedAt = DateTime.Now;

        _db.LogisticsRequests.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم نشر الطلب في سوق اللوجستيات.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var request = await _db.LogisticsRequests.AsNoTracking()
            .Include(r => r.Requester)
            .Include(r => r.Contract)
            .Include(r => r.Offers).ThenInclude(o => o.Provider)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null) return NotFound();

        ViewBag.IsOwner = request.RequesterId == Uid;
        ViewBag.MyOffer = request.Offers.FirstOrDefault(o => o.ProviderId == Uid);
        return View(request);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Offer(int id, decimal price, string? notes)
    {
        var request = await _db.LogisticsRequests.Include(r => r.Offers).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();

        if (request.Status != LogisticsRequestStatus.Open)
        {
            TempData["Error"] = "هذا الطلب لم يعد يستقبل عروضاً.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (request.RequesterId == Uid)
        {
            TempData["Error"] = "لا يمكنك تقديم عرض على طلبك.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (price <= 0)
        {
            TempData["Error"] = "أدخل سعراً صحيحاً.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var existing = request.Offers.FirstOrDefault(o => o.ProviderId == Uid);
        if (existing is not null)
        {
            existing.Price = price;
            existing.Notes = notes;
        }
        else
        {
            _db.LogisticsOffers.Add(new LogisticsOffer
            {
                LogisticsRequestId = id,
                ProviderId = Uid,
                Price = price,
                Notes = notes
            });
        }
        await _db.SaveChangesAsync();

        await _notify.PushAsync(request.RequesterId, "عرض نقل جديد",
            $"{request.FromLocation} ← {request.ToLocation} بسعر {price:N2} ر.ع.", $"/Logistics/Details/{id}");

        TempData["Success"] = "تم تسجيل عرضك.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Award(int id, int offerId)
    {
        var request = await _db.LogisticsRequests.Include(r => r.Offers).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.RequesterId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        var winner = request.Offers.FirstOrDefault(o => o.Id == offerId);
        if (winner is null) return NotFound();

        winner.Status = OfferStatus.Awarded;
        foreach (var o in request.Offers.Where(o => o.Id != offerId))
            o.Status = OfferStatus.Rejected;

        request.Status = LogisticsRequestStatus.Awarded;
        await _db.SaveChangesAsync();

        await _notify.PushAsync(winner.ProviderId, "تمت ترسية طلب النقل عليك",
            $"{request.FromLocation} ← {request.ToLocation}.", $"/Logistics/Details/{id}");

        TempData["Success"] = "تمت الترسية على مزود الخدمة.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var request = await _db.LogisticsRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();
        if (request.RequesterId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        request.Status = LogisticsRequestStatus.Completed;
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم إغلاق الطلب كمكتمل.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
