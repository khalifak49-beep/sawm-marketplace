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
public class AuctionsController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly NotificationService _notify;
    private readonly ContractService _contracts;
    private readonly MatchingService _matcher;
    private readonly BranchService _branch;

    public AuctionsController(SawmDbContext db, UserManager<ApplicationUser> users,
        NotificationService notify, ContractService contracts, MatchingService matcher, BranchService branch)
    {
        _db = db;
        _users = users;
        _notify = notify;
        _contracts = contracts;
        _matcher = matcher;
        _branch = branch;
    }

    private string Uid => _users.GetUserId(User)!;

    /// <summary>سوق المزادات — متاح لجميع الأدوار مع فلاتر</summary>
    [AllowAnonymous]
    public async Task<IActionResult> Index(int? cropId, AuctionType? type, string? region, string? q, bool mine = false)
    {
        var query = _db.Auctions.AsNoTracking()
            .Include(a => a.Crop).Include(a => a.Farmer).Include(a => a.Broker).Include(a => a.Bids)
            .AsQueryable();

        if (mine && User.Identity?.IsAuthenticated == true)
        {
            var uid = Uid;
            query = query.Where(a => a.FarmerId == uid || a.BrokerId == uid);
        }
        else
        {
            // الجمهور يرى النشط والمُرسى فقط
            query = query.Where(a => a.Status == AuctionStatus.Active || a.Status == AuctionStatus.Closed);
        }

        if (cropId is > 0) query = query.Where(a => a.CropId == cropId);
        if (type is not null) query = query.Where(a => a.Type == type);
        if (!string.IsNullOrWhiteSpace(region)) query = query.Where(a => a.Farmer!.Region == region);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(a => a.Title.Contains(q) || a.Crop!.Name.Contains(q));

        ViewBag.Crops = await CropSelectListAsync(cropId);
        ViewBag.Regions = await _db.Users.Where(u => u.Region != null).Select(u => u.Region!).Distinct().ToListAsync();
        ViewBag.CropId = cropId;
        ViewBag.Type = type;
        ViewBag.Region = region;
        ViewBag.Q = q;
        ViewBag.Mine = mine;

        var list = await query.OrderByDescending(a => a.Status == AuctionStatus.Active).ThenBy(a => a.EndDate).ToListAsync();
        return View(list);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        var auction = await _db.Auctions.AsNoTracking()
            .Include(a => a.Crop)
            .Include(a => a.Farmer)
            .Include(a => a.Broker)
            .Include(a => a.Bids).ThenInclude(b => b.Bidder)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (auction is null) return NotFound();

        // المزايدات المعلّقة (تجاوزت حد فرع) لم تدخل المزاد بعد — تُخفى عن الجميع في قائمة الترتيب.
        // صاحب المزايدة المعلّقة يرى حالتها في صفحته الخاصة/نشاط الفروع لدى الشركة الرئيسية.
        var ranked = _matcher.Rank(auction.Bids.Where(b => b.Status != BidStatus.PendingApproval)).ToList();
        ViewBag.RankedBids = ranked;
        ViewBag.CanBid = User.IsInRole(Roles.Company) || User.IsInRole(Roles.Broker);
        ViewBag.IsOwner = User.Identity?.IsAuthenticated == true && auction.FarmerId == Uid;
        ViewBag.IsManagingBroker = User.Identity?.IsAuthenticated == true && auction.BrokerId == Uid;

        // ── إخفاء هوية المزايدين ──────────────────────────────────
        // المزايدات مجهولة الهوية للجميع، وتُكشف فقط لـ:
        //   • الإدارة (كل الهويات)   • كل مزايد لصفّه هو
        //   • الشركة الأم لهويات فروعها فقط (لا منافسيها)
        var uid = User.Identity?.IsAuthenticated == true ? Uid : null;
        ViewBag.RevealAll = User.IsInRole(Roles.Admin);
        var revealIds = new HashSet<string>();
        if (uid is not null)
        {
            revealIds.Add(uid);
            var myBranches = await _db.CompanyProfiles.AsNoTracking()
                .Where(p => p.ParentCompanyId == uid).Select(p => p.UserId).ToListAsync();
            foreach (var branchId in myBranches) revealIds.Add(branchId);
        }
        ViewBag.RevealIds = revealIds;

        // اسم مستعار ثابت لكل مزايد مختلف (مزايد ١، ٢...) بترتيب أول ظهور — يبيّن عدد
        // المزايدين المتمايزين دون كشف الهوية.
        var alias = new Dictionary<string, int>();
        foreach (var b in ranked)
            if (!alias.ContainsKey(b.BidderId)) alias[b.BidderId] = alias.Count + 1;
        ViewBag.BidderAlias = alias;

        return View(auction);
    }

    // ── إنشاء مزاد: المزارع أو الوسيط نيابة عن مزارع ──────────────────────────
    [Authorize(Roles = Roles.Farmer + "," + Roles.Broker + "," + Roles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await FillCreateListsAsync();
        return View(new Auction
        {
            StartDate = DateTime.Now,
            EndDate = DateTime.Now.AddDays(7)
        });
    }

    [Authorize(Roles = Roles.Farmer + "," + Roles.Broker + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Auction model, string? newCropName)
    {
        ModelState.Remove(nameof(Auction.FarmerId));
        ModelState.Remove(nameof(Auction.BrokerId));

        // اختيار "أخرى" في قائمة المحصول = CropId == -1 → يتطلب اسم محصول جديد
        if (model.CropId == -1 && string.IsNullOrWhiteSpace(newCropName))
            ModelState.AddModelError("newCropName", "اكتب اسم المحصول الجديد.");
        else if (model.CropId == 0)
            ModelState.AddModelError(nameof(model.CropId), "اختر المحصول.");

        if (model.EndDate <= model.StartDate)
            ModelState.AddModelError(nameof(model.EndDate), "تاريخ الإغلاق يجب أن يكون بعد تاريخ البدء.");

        if (model.Type == AuctionType.Future && model.ExpectedHarvestDate is null)
            ModelState.AddModelError(nameof(model.ExpectedHarvestDate), "المزاد المستقبلي يتطلب تاريخ حصاد متوقع.");

        if (User.IsInRole(Roles.Farmer))
        {
            model.FarmerId = Uid;
        }
        else if (string.IsNullOrWhiteSpace(model.FarmerId))
        {
            ModelState.AddModelError(nameof(model.FarmerId), "اختر المزارع صاحب المحصول.");
        }

        // الوسيط الذي ينشئ المزاد يصبح المشرف عليه، لكن كل المزادات تنتظر اعتماد الإدارة
        if (User.IsInRole(Roles.Broker)) model.BrokerId = Uid;
        model.Status = AuctionStatus.Pending;   // بانتظار تدقيق واعتماد الإدارة

        // ModelState لوحدة الكمية غير مطلوبة من المستخدم — تُضبط دائماً بالطن
        ModelState.Remove(nameof(Auction.QuantityUnit));

        if (!ModelState.IsValid)
        {
            ViewBag.NewCropName = newCropName;   // حافظ على الاسم المُدخل عند إعادة العرض
            await FillCreateListsAsync();
            return View(model);
        }

        // إضافة محصول جديد عند اختيار "أخرى" (بعد اجتياز كل التحقق) — أو إعادة استخدام محصول بنفس الاسم
        if (model.CropId == -1)
        {
            var name = newCropName!.Trim();
            var crop = await _db.Crops.FirstOrDefaultAsync(c => c.Name == name);
            if (crop is null)
            {
                crop = new Crop { Name = name, Category = "أخرى", Unit = "طن", IsActive = true };
                _db.Crops.Add(crop);
                await _db.SaveChangesAsync();
            }
            model.CropId = crop.Id;
        }

        model.QuantityUnit = "طن";   // كل المزادات الجديدة كميتها بالطن
        model.CreatedAt = DateTime.Now;
        _db.Auctions.Add(model);
        await _db.SaveChangesAsync();

        // إشعار الإدارة بوجود مزاد يحتاج تدقيقاً واعتماداً
        var adminIds = await _db.Users.Where(u => u.UserType == UserType.Admin).Select(u => u.Id).ToListAsync();
        await _notify.PushManyAsync(adminIds, "مزاد جديد بانتظار اعتماد الإدارة",
            $"{model.Title} — يحتاج تدقيقاً قبل فتحه للمزايدة.", "/Admin/PendingAuctions");

        TempData["Success"] = "تم إنشاء المزاد. سيُفتح للمزايدة بعد تدقيق واعتماد الإدارة.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // ── اعتماد الإدارة للمزاد (بعد التدقيق) ──────────────────────────────────
    [Authorize(Roles = Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var auction = await _db.Auctions.FindAsync(id);
        if (auction is null) return NotFound();

        if (auction.Status != AuctionStatus.Pending)
        {
            TempData["Error"] = "لا يمكن اعتماد هذا المزاد في حالته الحالية.";
            return RedirectToAction(nameof(Details), new { id });
        }

        auction.Status = AuctionStatus.Active;
        if (auction.StartDate > DateTime.Now) auction.StartDate = DateTime.Now;
        await _db.SaveChangesAsync();

        await _notify.PushManyAsync(new[] { auction.FarmerId, auction.BrokerId ?? "" },
            "تم اعتماد مزادك", $"اعتمدت الإدارة مزاد \"{auction.Title}\" وأصبح مفتوحاً للمزايدة.", $"/Auctions/Details/{id}");

        // إشعار الشركات المهتمة بهذا المحصول
        var companyIds = await _db.Tenders.Where(t => t.CropId == auction.CropId)
            .Select(t => t.CompanyId).Distinct().ToListAsync();
        await _notify.PushManyAsync(companyIds, "مزاد جديد على محصول تتابعه",
            auction.Title, $"/Auctions/Details/{id}");

        TempData["Success"] = "تم اعتماد المزاد وفتحه للمزايدة.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── رفض الإدارة للمزاد ───────────────────────────────────────────────────
    [Authorize(Roles = Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var auction = await _db.Auctions.FindAsync(id);
        if (auction is null) return NotFound();

        if (auction.Status != AuctionStatus.Pending)
        {
            TempData["Error"] = "لا يمكن رفض هذا المزاد في حالته الحالية.";
            return RedirectToAction(nameof(Details), new { id });
        }

        auction.Status = AuctionStatus.Cancelled;
        await _db.SaveChangesAsync();

        await _notify.PushManyAsync(new[] { auction.FarmerId, auction.BrokerId ?? "" },
            "لم يُعتمد مزادك", $"رفضت الإدارة مزاد \"{auction.Title}\". {(string.IsNullOrWhiteSpace(reason) ? "" : reason)}",
            $"/Auctions/Details/{id}");

        TempData["Success"] = "تم رفض المزاد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── تبنّي الوسيط للإشراف على مزاد (يبقى بانتظار اعتماد الإدارة) ──────────
    [Authorize(Roles = Roles.Broker)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Adopt(int id)
    {
        var auction = await _db.Auctions.FindAsync(id);
        if (auction is null) return NotFound();

        if (auction.Status != AuctionStatus.Pending)
        {
            TempData["Error"] = "لا يمكن تبنّي هذا المزاد في حالته الحالية.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!string.IsNullOrEmpty(auction.BrokerId) && auction.BrokerId != Uid)
        {
            TempData["Error"] = "لهذا المزاد وسيط مشرف بالفعل.";
            return RedirectToAction(nameof(Details), new { id });
        }

        auction.BrokerId = Uid;
        await _db.SaveChangesAsync();

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        await _notify.PushAsync(auction.FarmerId, "وسيط تبنّى الإشراف على مزادك",
            $"{me.FullName} سيشرف على \"{auction.Title}\" — بانتظار اعتماد الإدارة.", $"/Auctions/Details/{id}");
        var adminIds = await _db.Users.Where(u => u.UserType == UserType.Admin).Select(u => u.Id).ToListAsync();
        await _notify.PushManyAsync(adminIds, "مزاد تبنّاه وسيط — بانتظار اعتمادكم",
            auction.Title, "/Admin/PendingAuctions");

        TempData["Success"] = "تبنّيت الإشراف على المزاد. سيُفتح بعد اعتماد الإدارة.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Farmer + "," + Roles.Broker + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var auction = await _db.Auctions.Include(a => a.Bids).FirstOrDefaultAsync(a => a.Id == id);
        if (auction is null) return NotFound();

        if (auction.FarmerId != Uid && auction.BrokerId != Uid && !User.IsInRole(Roles.Admin))
            return Forbid();

        if (auction.Status == AuctionStatus.Closed)
        {
            TempData["Error"] = "لا يمكن إلغاء مزاد تمت ترسيته.";
            return RedirectToAction(nameof(Details), new { id });
        }

        auction.Status = AuctionStatus.Cancelled;
        await _db.SaveChangesAsync();

        await _notify.PushManyAsync(auction.Bids.Select(b => b.BidderId).Append(auction.FarmerId),
            "تم إلغاء المزاد", auction.Title, $"/Auctions/Details/{id}");

        TempData["Success"] = "تم إلغاء المزاد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── المزايدة: الشركة أو الوسيط ─────────────────────────────────────────
    [Authorize(Roles = Roles.Company + "," + Roles.Broker)]
    [HttpGet]
    public async Task<IActionResult> Bid(int id)
    {
        var auction = await _db.Auctions.AsNoTracking()
            .Include(a => a.Crop).Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (auction is null) return NotFound();
        if (!auction.IsLive)
        {
            TempData["Error"] = "هذا المزاد غير مفتوح للمزايدة حالياً.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var current = auction.CurrentPrice;
        return View(new PlaceBidViewModel
        {
            AuctionId = auction.Id,
            AuctionTitle = auction.Title,
            CurrentPrice = current,
            MinIncrement = auction.MinIncrement,
            Quantity = auction.Quantity,
            Unit = auction.DisplayUnit,
            UnitPrice = current + auction.MinIncrement
        });
    }

    [Authorize(Roles = Roles.Company + "," + Roles.Broker)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Bid(PlaceBidViewModel model)
    {
        var auction = await _db.Auctions.Include(a => a.Bids).Include(a => a.Crop)
            .FirstOrDefaultAsync(a => a.Id == model.AuctionId);

        if (auction is null) return NotFound();

        if (!auction.IsLive)
            ModelState.AddModelError(string.Empty, "انتهت نافذة المزايدة على هذا المزاد.");

        if (auction.FarmerId == Uid)
            ModelState.AddModelError(string.Empty, "لا يمكنك المزايدة على مزادك الخاص.");

        var current = auction.CurrentPrice;
        var minAcceptable = auction.Bids.Count == 0 ? auction.StartPrice : current + auction.MinIncrement;

        if (ModelState.IsValid && model.UnitPrice < minAcceptable)
            ModelState.AddModelError(nameof(model.UnitPrice),
                $"أقل مزايدة مقبولة هي {minAcceptable:N2} للوحدة.");

        // فحص صلاحية الفرع + حد المزايدة (إن كان المزايد فرع شركة)
        var profile = await _branch.ProfileAsync(Uid);
        if (profile is { IsBranch: true } && !profile.CanBid)
            ModelState.AddModelError(string.Empty, "لا تملك صلاحية المزايدة. تواصل مع الشركة الرئيسية.");

        if (!ModelState.IsValid)
        {
            model.AuctionTitle = auction.Title;
            model.CurrentPrice = current;
            model.MinIncrement = auction.MinIncrement;
            model.Quantity = auction.Quantity;
            model.Unit = auction.DisplayUnit;
            return View(model);
        }

        var totalValue = model.UnitPrice * auction.Quantity;

        // ── مزايدة فرع تجاوزت الحد → تُعلَّق بانتظار موافقة الرئيسية ──
        if (BranchService.NeedsApproval(profile, totalValue))
        {
            _db.Bids.Add(new Bid
            {
                AuctionId = auction.Id,
                BidderId = Uid,
                UnitPrice = model.UnitPrice,
                Notes = model.Notes,
                Status = BidStatus.PendingApproval,
                TotalValueAtBid = totalValue
            });
            await _db.SaveChangesAsync();

            await _notify.PushAsync(profile!.ParentCompanyId!, "مزايدة فرع تتجاوز الحد — تنتظر موافقتك",
                $"{profile.CompanyName}: {model.UnitPrice:N2} للوحدة (إجمالي {totalValue:N2}) — الحد {profile.BidLimit:N2}. المزاد: {auction.Title}",
                "/Company/Activity");

            TempData["Success"] = $"مزايدتك ({totalValue:N2}) تجاوزت حدّك ({profile.BidLimit:N2})، فأُرسلت لموافقة الشركة الرئيسية ولم تدخل المزاد بعد.";
            return RedirectToAction(nameof(Details), new { id = auction.Id });
        }

        // تنزيل حالة المزايدة السابقة الأعلى
        foreach (var b in auction.Bids.Where(b => b.Status == BidStatus.Winning))
            b.Status = BidStatus.Submitted;

        var bid = new Bid
        {
            AuctionId = auction.Id,
            BidderId = Uid,
            UnitPrice = model.UnitPrice,
            Notes = model.Notes,
            Status = BidStatus.Winning,
            TotalValueAtBid = totalValue
        };
        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();

        await _notify.PushAsync(auction.FarmerId, "مزايدة جديدة على مزادك",
            $"{auction.Title} — سعر جديد {model.UnitPrice:N2} للوحدة.", $"/Auctions/Details/{auction.Id}");

        if (!string.IsNullOrEmpty(auction.BrokerId))
            await _notify.PushAsync(auction.BrokerId, "مزايدة جديدة على مزاد تشرف عليه",
                $"{auction.Title} — {model.UnitPrice:N2} للوحدة.", $"/Auctions/Details/{auction.Id}");

        // إشعار الفرع لدى الشركة الرئيسية عند مزايدة ضمن الحد (رؤية كاملة لنشاط الفرع)
        if (profile is { IsBranch: true })
            await _notify.PushAsync(profile.ParentCompanyId!, "مزايدة فرع (ضمن الحد)",
                $"{profile.CompanyName}: {model.UnitPrice:N2} للوحدة على \"{auction.Title}\".", "/Company/Activity");

        // إشعار من كان الأعلى سابقاً بأنه تُجووز
        var outbid = auction.LiveBids.Where(b => b.BidderId != Uid).Select(b => b.BidderId).Distinct().ToList();
        await _notify.PushManyAsync(outbid, "تم تجاوز مزايدتك",
            $"{auction.Title} — السعر الحالي {model.UnitPrice:N2}.", $"/Auctions/Details/{auction.Id}");

        TempData["Success"] = "تم تسجيل مزايدتك.";
        return RedirectToAction(nameof(Details), new { id = auction.Id });
    }

    // ── الترسية: المزارع أو الوسيط المشرف يقبل المزايدة الفائزة ──────────────
    [Authorize(Roles = Roles.Farmer + "," + Roles.Broker + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptBid(int auctionId, int bidId)
    {
        var auction = await _db.Auctions.Include(a => a.Bids).FirstOrDefaultAsync(a => a.Id == auctionId);
        if (auction is null) return NotFound();

        if (auction.FarmerId != Uid && auction.BrokerId != Uid && !User.IsInRole(Roles.Admin))
            return Forbid();

        if (auction.Status == AuctionStatus.Closed)
        {
            TempData["Error"] = "تمت ترسية هذا المزاد مسبقاً.";
            return RedirectToAction(nameof(Details), new { id = auctionId });
        }

        var winning = auction.Bids.FirstOrDefault(b => b.Id == bidId);
        if (winning is null) return NotFound();

        if (winning.Status == BidStatus.PendingApproval)
        {
            TempData["Error"] = "هذه المزايدة معلّقة بانتظار موافقة الشركة الرئيسية ولا يمكن ترسيتها بعد.";
            return RedirectToAction(nameof(Details), new { id = auctionId });
        }

        winning.Status = BidStatus.Accepted;
        foreach (var b in auction.Bids.Where(b => b.Id != bidId))
            b.Status = BidStatus.Rejected;

        auction.Status = AuctionStatus.Closed;
        await _db.SaveChangesAsync();

        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        var contract = await _contracts.CreateFromAuctionAsync(auction, winning, Uid, user.FullName);

        if (!string.IsNullOrEmpty(auction.BrokerId))
        {
            var bp = await _db.BrokerProfiles.FirstOrDefaultAsync(p => p.UserId == auction.BrokerId);
            if (bp is not null) { bp.ClosedDeals++; await _db.SaveChangesAsync(); }
        }

        TempData["Success"] = $"تمت الترسية وإنشاء العقد {contract.ContractNumber}.";
        return RedirectToAction("Details", "Contracts", new { id = contract.Id });
    }

    // ── تعديل تفاصيل المزاد (تدقيق الإدارة) ─────────────────────────────────
    [Authorize(Roles = Roles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var auction = await _db.Auctions.FindAsync(id);
        if (auction is null) return NotFound();
        if (auction.Status == AuctionStatus.Closed)
        {
            TempData["Error"] = "لا يمكن تعديل مزاد تمت ترسيته.";
            return RedirectToAction(nameof(Details), new { id });
        }
        await FillEditListsAsync(auction);
        return View(auction);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Auction model)
    {
        var auction = await _db.Auctions.FindAsync(model.Id);
        if (auction is null) return NotFound();
        if (auction.Status == AuctionStatus.Closed)
        {
            TempData["Error"] = "لا يمكن تعديل مزاد تمت ترسيته.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        ModelState.Remove(nameof(Auction.FarmerId));
        ModelState.Remove(nameof(Auction.QuantityUnit));
        if (model.EndDate <= model.StartDate)
            ModelState.AddModelError(nameof(model.EndDate), "تاريخ الإغلاق يجب أن يكون بعد تاريخ البدء.");
        if (model.Type == AuctionType.Future && model.ExpectedHarvestDate is null)
            ModelState.AddModelError(nameof(model.ExpectedHarvestDate), "المزاد المستقبلي يتطلب تاريخ حصاد متوقع.");
        if (model.CropId <= 0)
            ModelState.AddModelError(nameof(model.CropId), "اختر المحصول.");

        if (!ModelState.IsValid)
        {
            await FillEditListsAsync(model);
            return View(model);
        }

        // تحديث الحقول القابلة للتدقيق فقط
        auction.Title = model.Title;
        auction.CropId = model.CropId;
        auction.Quantity = model.Quantity;
        auction.Quality = model.Quality;
        auction.StartPrice = model.StartPrice;
        auction.MinIncrement = model.MinIncrement;
        auction.Type = model.Type;
        auction.StartDate = model.StartDate;
        auction.EndDate = model.EndDate;
        auction.ExpectedHarvestDate = model.ExpectedHarvestDate;
        auction.PickupLocation = model.PickupLocation;
        auction.Logistics = model.Logistics;
        auction.Description = model.Description;
        auction.BrokerId = string.IsNullOrWhiteSpace(model.BrokerId) ? null : model.BrokerId;
        await _db.SaveChangesAsync();

        await _notify.PushManyAsync(new[] { auction.FarmerId, auction.BrokerId ?? "" },
            "عدّلت الإدارة تفاصيل مزادك", auction.Title, $"/Auctions/Details/{auction.Id}");

        TempData["Success"] = "تم حفظ تعديلات المزاد.";
        return RedirectToAction(nameof(Details), new { id = auction.Id });
    }

    private async Task FillCreateListsAsync()
    {
        ViewBag.Crops = await CropSelectListAsync(null);
        ViewBag.Farmers = await _db.Users.AsNoTracking()
            .Where(u => u.UserType == UserType.Farmer)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem { Value = u.Id, Text = u.FullName + " — " + (u.Region ?? "") })
            .ToListAsync();
    }

    private async Task FillEditListsAsync(Auction auction)
    {
        ViewBag.Crops = await CropSelectListAsync(auction.CropId);
        ViewBag.Brokers = await _db.Users.AsNoTracking()
            .Where(u => u.UserType == UserType.Broker)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem
            {
                Value = u.Id,
                Text = u.FullName + " — " + (u.Region ?? ""),
                Selected = u.Id == auction.BrokerId
            }).ToListAsync();
        ViewBag.FarmerName = await _db.Users.Where(u => u.Id == auction.FarmerId)
            .Select(u => u.FullName).FirstOrDefaultAsync();
    }

    private async Task<List<SelectListItem>> CropSelectListAsync(int? selected) =>
        await _db.Crops.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name,
                Selected = selected.HasValue && c.Id == selected.Value
            }).ToListAsync();
}
