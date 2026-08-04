using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

[Authorize]
public class TendersController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly NotificationService _notify;
    private readonly ContractService _contracts;
    private readonly MatchingService _matcher;
    private readonly BranchService _branch;

    public TendersController(SawmDbContext db, UserManager<ApplicationUser> users,
        NotificationService notify, ContractService contracts, MatchingService matcher, BranchService branch)
    {
        _db = db;
        _users = users;
        _notify = notify;
        _contracts = contracts;
        _matcher = matcher;
        _branch = branch;
    }

    /// <summary>يمنع الفرع من فعل تتطلب صلاحية لا يملكها. يعيد رسالة الخطأ أو null.</summary>
    private async Task<string?> BranchBlockAsync(Func<CompanyProfile, bool> allowed, string action)
    {
        var p = await _branch.ProfileAsync(Uid);
        if (p is { IsBranch: true } && !allowed(p))
            return $"لا تملك صلاحية {action}. تواصل مع الشركة الرئيسية.";
        return null;
    }

    private string Uid => _users.GetUserId(User)!;

    [AllowAnonymous]
    public async Task<IActionResult> Index(int? cropId, string? q, bool mine = false)
    {
        var query = _db.Tenders.AsNoTracking()
            .Include(t => t.Crop).Include(t => t.Company).Include(t => t.Offers)
            .AsQueryable();

        if (mine && User.Identity?.IsAuthenticated == true)
        {
            var uid = Uid;
            query = query.Where(t => t.CompanyId == uid || t.Offers.Any(o => o.SupplierId == uid || o.BrokerId == uid));
        }

        if (cropId is > 0) query = query.Where(t => t.CropId == cropId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(t => t.Title.Contains(q) || t.Crop!.Name.Contains(q));

        ViewBag.Crops = await _db.Crops.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name, Selected = cropId == c.Id })
            .ToListAsync();
        ViewBag.CropId = cropId;
        ViewBag.Q = q;
        ViewBag.Mine = mine;

        var list = await query
            .OrderByDescending(t => t.Status == TenderStatus.Open)
            .ThenBy(t => t.ClosingDate)
            .ToListAsync();
        return View(list);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        var tender = await _db.Tenders.AsNoTracking()
            .Include(t => t.Crop)
            .Include(t => t.Company)
            .Include(t => t.Offers).ThenInclude(o => o.Supplier)
            .Include(t => t.Offers).ThenInclude(o => o.Broker)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender is null) return NotFound();

        var isOwner = User.Identity?.IsAuthenticated == true && tender.CompanyId == Uid;
        ViewBag.IsOwner = isOwner;
        ViewBag.CanOffer = User.IsInRole(Roles.Farmer) || User.IsInRole(Roles.Broker);

        // صاحب المناقصة والإدارة يريان كل العروض؛ غيرهم يرى عرضه فقط
        var offers = tender.Offers.OrderByDescending(o => o.MatchScore).ThenBy(o => o.UnitPrice).ToList();
        if (!isOwner && !User.IsInRole(Roles.Admin))
        {
            var uid = User.Identity?.IsAuthenticated == true ? Uid : null;
            offers = offers.Where(o => o.SupplierId == uid || o.BrokerId == uid).ToList();
        }
        ViewBag.VisibleOffers = offers;
        ViewBag.OffersCount = tender.Offers.Count;

        return View(tender);
    }

    [Authorize(Roles = Roles.Company + "," + Roles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var block = await BranchBlockAsync(p => p.CanCreateTenders, "طرح المناقصات");
        if (block is not null) { TempData["Error"] = block; return RedirectToAction(nameof(Index)); }

        await FillCropsAsync();
        return View(new Tender
        {
            ClosingDate = DateTime.Now.AddDays(10),
            DeliveryDate = DateTime.Now.AddDays(30)
        });
    }

    [Authorize(Roles = Roles.Company + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Tender model)
    {
        var block = await BranchBlockAsync(p => p.CanCreateTenders, "طرح المناقصات");
        if (block is not null) { TempData["Error"] = block; return RedirectToAction(nameof(Index)); }

        ModelState.Remove(nameof(Tender.CompanyId));

        if (model.ClosingDate >= model.DeliveryDate)
            ModelState.AddModelError(nameof(model.ClosingDate), "موعد إغلاق العروض يجب أن يسبق تاريخ التسليم.");

        if (!ModelState.IsValid)
        {
            await FillCropsAsync();
            return View(model);
        }

        model.CompanyId = Uid;
        model.Status = TenderStatus.Open;
        model.CreatedAt = DateTime.Now;

        _db.Tenders.Add(model);
        await _db.SaveChangesAsync();

        // إشعار المزارعين والوسطاء المرتبطين بهذا المحصول
        var interestedFarmers = await _db.Auctions.Where(a => a.CropId == model.CropId)
            .Select(a => a.FarmerId).Distinct().ToListAsync();
        var brokers = await _db.Users.Where(u => u.UserType == UserType.Broker).Select(u => u.Id).ToListAsync();

        await _notify.PushManyAsync(interestedFarmers.Concat(brokers),
            "مناقصة جديدة مفتوحة",
            $"{model.Title} — آخر موعد للعروض {model.ClosingDate:yyyy/MM/dd}.",
            $"/Tenders/Details/{model.Id}");

        TempData["Success"] = "تم نشر المناقصة.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // ── تقديم عرض: مزارع مباشرة، أو وسيط نيابة عن مزارع ─────────────────────
    [Authorize(Roles = Roles.Farmer + "," + Roles.Broker)]
    [HttpGet]
    public async Task<IActionResult> Offer(int id)
    {
        var tender = await _db.Tenders.AsNoTracking().Include(t => t.Crop).FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();

        if (!tender.IsOpen)
        {
            TempData["Error"] = "المناقصة مغلقة ولا تستقبل عروضاً.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var uid = Uid;
        var already = await _db.TenderOffers.AnyAsync(o => o.TenderId == id && (o.SupplierId == uid || o.BrokerId == uid));
        if (already)
        {
            TempData["Error"] = "لديك عرض مقدَّم على هذه المناقصة بالفعل.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await FillFarmersAsync();
        return View(new SubmitOfferViewModel
        {
            TenderId = tender.Id,
            TenderTitle = tender.Title,
            RequiredQuantity = tender.Quantity,
            MaxUnitPrice = tender.MaxUnitPrice,
            Unit = tender.Crop?.Unit ?? "كجم",
            AvailableQuantity = tender.Quantity,
            EarliestDelivery = tender.DeliveryDate
        });
    }

    [Authorize(Roles = Roles.Farmer + "," + Roles.Broker)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Offer(SubmitOfferViewModel model)
    {
        var tender = await _db.Tenders.Include(t => t.Crop).FirstOrDefaultAsync(t => t.Id == model.TenderId);
        if (tender is null) return NotFound();

        if (!tender.IsOpen)
            ModelState.AddModelError(string.Empty, "المناقصة مغلقة ولا تستقبل عروضاً.");

        var uid = Uid;
        var isBroker = User.IsInRole(Roles.Broker);

        if (isBroker && string.IsNullOrWhiteSpace(model.RepresentedFarmerId))
            ModelState.AddModelError(nameof(model.RepresentedFarmerId), "اختر المزارع الذي تمثله في هذا العرض.");

        if (tender.MaxUnitPrice > 0 && model.UnitPrice > tender.MaxUnitPrice)
            ModelState.AddModelError(nameof(model.UnitPrice),
                $"السعر يتجاوز السقف المعلن ({tender.MaxUnitPrice:N2}).");

        if (!ModelState.IsValid)
        {
            model.TenderTitle = tender.Title;
            model.RequiredQuantity = tender.Quantity;
            model.MaxUnitPrice = tender.MaxUnitPrice;
            model.Unit = tender.Crop?.Unit ?? "كجم";
            await FillFarmersAsync();
            return View(model);
        }

        var supplierId = isBroker ? model.RepresentedFarmerId! : uid;

        var offer = new TenderOffer
        {
            TenderId = tender.Id,
            SupplierId = supplierId,
            BrokerId = isBroker ? uid : null,
            UnitPrice = model.UnitPrice,
            AvailableQuantity = model.AvailableQuantity,
            EarliestDelivery = model.EarliestDelivery,
            Notes = model.Notes
        };

        var supplier = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == supplierId);
        offer.MatchScore = _matcher.ScoreOffer(tender, offer, supplier);

        _db.TenderOffers.Add(offer);
        await _db.SaveChangesAsync();

        await _notify.PushAsync(tender.CompanyId, "عرض جديد على مناقصتك",
            $"{tender.Title} — سعر {offer.UnitPrice:N2} بدرجة مطابقة {offer.MatchScore:N0}%.",
            $"/Tenders/Details/{tender.Id}");

        if (isBroker)
            await _notify.PushAsync(supplierId, "وسيط قدّم عرضاً نيابة عنك",
                $"{tender.Title} — سعر {offer.UnitPrice:N2}.", $"/Tenders/Details/{tender.Id}");

        TempData["Success"] = $"تم تقديم العرض. درجة المطابقة الآلية: {offer.MatchScore:N0}%.";
        return RedirectToAction(nameof(Details), new { id = tender.Id });
    }

    // ── الترسية على العرض الفائز ────────────────────────────────────────────
    [Authorize(Roles = Roles.Company + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Award(int tenderId, int offerId)
    {
        var tender = await _db.Tenders.Include(t => t.Offers).FirstOrDefaultAsync(t => t.Id == tenderId);
        if (tender is null) return NotFound();

        if (tender.CompanyId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        if (tender.Status == TenderStatus.Awarded)
        {
            TempData["Error"] = "تمت ترسية هذه المناقصة مسبقاً.";
            return RedirectToAction(nameof(Details), new { id = tenderId });
        }

        var winning = tender.Offers.FirstOrDefault(o => o.Id == offerId);
        if (winning is null) return NotFound();

        winning.Status = OfferStatus.Awarded;
        foreach (var o in tender.Offers.Where(o => o.Id != offerId))
            o.Status = OfferStatus.Rejected;

        tender.Status = TenderStatus.Awarded;
        await _db.SaveChangesAsync();

        var actor = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        var contract = await _contracts.CreateFromTenderAsync(tender, winning, Uid, actor.FullName);

        if (!string.IsNullOrEmpty(winning.BrokerId))
        {
            var bp = await _db.BrokerProfiles.FirstOrDefaultAsync(p => p.UserId == winning.BrokerId);
            if (bp is not null) { bp.ClosedDeals++; await _db.SaveChangesAsync(); }
        }

        var losers = tender.Offers.Where(o => o.Id != offerId).Select(o => o.SupplierId).ToList();
        await _notify.PushManyAsync(losers, "لم يُرسَ عرضك",
            $"تمت ترسية المناقصة \"{tender.Title}\" على عرض آخر.", $"/Tenders/Details/{tenderId}");

        TempData["Success"] = $"تمت الترسية وإنشاء العقد {contract.ContractNumber}.";
        return RedirectToAction("Details", "Contracts", new { id = contract.Id });
    }

    [Authorize(Roles = Roles.Company + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Shortlist(int tenderId, int offerId)
    {
        var offer = await _db.TenderOffers.Include(o => o.Tender).FirstOrDefaultAsync(o => o.Id == offerId && o.TenderId == tenderId);
        if (offer is null) return NotFound();
        if (offer.Tender!.CompanyId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        offer.Status = OfferStatus.Shortlisted;
        offer.Tender.Status = TenderStatus.UnderReview;
        await _db.SaveChangesAsync();

        await _notify.PushAsync(offer.SupplierId, "عرضك ضمن القائمة المختصرة",
            offer.Tender.Title, $"/Tenders/Details/{tenderId}");

        TempData["Success"] = "تم إدراج العرض في القائمة المختصرة.";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [Authorize(Roles = Roles.Company + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelTender(int id)
    {
        var tender = await _db.Tenders.Include(t => t.Offers).FirstOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        if (tender.CompanyId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        if (tender.Status == TenderStatus.Awarded)
        {
            TempData["Error"] = "لا يمكن إلغاء مناقصة تمت ترسيتها.";
            return RedirectToAction(nameof(Details), new { id });
        }

        tender.Status = TenderStatus.Cancelled;
        await _db.SaveChangesAsync();

        await _notify.PushManyAsync(tender.Offers.Select(o => o.SupplierId),
            "تم إلغاء المناقصة", tender.Title, $"/Tenders/Details/{id}");

        TempData["Success"] = "تم إلغاء المناقصة.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task FillCropsAsync() =>
        ViewBag.Crops = await _db.Crops.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync();

    private async Task FillFarmersAsync() =>
        ViewBag.Farmers = await _db.Users.AsNoTracking().Where(u => u.UserType == UserType.Farmer)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem { Value = u.Id, Text = u.FullName + " — " + (u.Region ?? "") })
            .ToListAsync();
}
