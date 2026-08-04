using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>إدارة فروع الشركة الرئيسية: الإنشاء، الصلاحيات، حدود المزايدة، والموافقات.</summary>
[Authorize(Roles = Roles.Company + "," + Roles.Admin)]
public class CompanyController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly NotificationService _notify;
    private readonly BranchService _branch;

    public CompanyController(SawmDbContext db, UserManager<ApplicationUser> users,
        NotificationService notify, BranchService branch)
    {
        _db = db;
        _users = users;
        _notify = notify;
        _branch = branch;
    }

    private string Uid => _users.GetUserId(User)!;

    /// <summary>الشركة الحالية يجب أن تكون رئيسية (لا فرعاً) لإدارة الفروع.</summary>
    private async Task<CompanyProfile?> EnsureParentAsync()
    {
        var me = await _branch.ProfileAsync(Uid);
        return me is { IsBranch: false } ? me : null;
    }

    // ── قائمة الفروع + الموافقات المعلّقة ────────────────────────
    public async Task<IActionResult> Branches()
    {
        var me = await EnsureParentAsync();
        if (me is null)
        {
            TempData["Error"] = "إدارة الفروع متاحة للشركة الرئيسية فقط، وأنت مسجّل كفرع.";
            return RedirectToAction("Index", "Home");
        }

        var branches = await _branch.BranchesAsync(Uid);
        ViewBag.PendingCount = await _db.Bids
            .CountAsync(b => b.Status == BidStatus.PendingApproval
                && _db.CompanyProfiles.Any(p => p.UserId == b.BidderId && p.ParentCompanyId == Uid));
        return View(branches);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBranch(string branchName, string email, string password,
        string? region, decimal bidLimit, bool canBid, bool canSubmitOffers, bool canCreateTenders, bool canManageContracts)
    {
        var me = await EnsureParentAsync();
        if (me is null) return Forbid();

        if (string.IsNullOrWhiteSpace(branchName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "اسم الفرع والبريد وكلمة المرور حقول مطلوبة.";
            return RedirectToAction(nameof(Branches));
        }

        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
            FullName = branchName.Trim(),
            UserType = UserType.Company,
            Region = region,
            IsVerified = me.User?.IsVerified ?? false
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            TempData["Error"] = string.Join(" · ", created.Errors.Select(e =>
                e.Code is "DuplicateUserName" or "DuplicateEmail" ? "هذا البريد مسجّل مسبقاً" : e.Description));
            return RedirectToAction(nameof(Branches));
        }

        await _users.AddToRoleAsync(user, Roles.Company);

        _db.CompanyProfiles.Add(new CompanyProfile
        {
            UserId = user.Id,
            CompanyName = branchName.Trim(),
            ActivityType = me.ActivityType,
            ParentCompanyId = Uid,
            CanBid = canBid,
            CanSubmitOffers = canSubmitOffers,
            CanCreateTenders = canCreateTenders,
            CanManageContracts = canManageContracts,
            BidLimit = Math.Max(0, bidLimit)
        });
        _db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Title = "تم إنشاء حساب فرعك",
            Body = $"أنشأت \"{me.CompanyName}\" هذا الحساب كفرع تابع لها.",
            Url = "/"
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم إنشاء الفرع \"{branchName}\".";
        return RedirectToAction(nameof(Branches));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBranch(string branchUserId, decimal bidLimit,
        bool canBid, bool canSubmitOffers, bool canCreateTenders, bool canManageContracts)
    {
        var me = await EnsureParentAsync();
        if (me is null) return Forbid();

        var branch = await _db.CompanyProfiles.FirstOrDefaultAsync(p => p.UserId == branchUserId && p.ParentCompanyId == Uid);
        if (branch is null) return NotFound();

        branch.CanBid = canBid;
        branch.CanSubmitOffers = canSubmitOffers;
        branch.CanCreateTenders = canCreateTenders;
        branch.CanManageContracts = canManageContracts;
        branch.BidLimit = Math.Max(0, bidLimit);
        await _db.SaveChangesAsync();

        await _notify.PushAsync(branchUserId, "تم تحديث صلاحيات فرعك",
            $"حدّثت الشركة الرئيسية حدّ المزايدة والصلاحيات.", "/");

        TempData["Success"] = "تم تحديث صلاحيات الفرع وحدّه.";
        return RedirectToAction(nameof(Branches));
    }

    // ── نشاط الفروع داخل المزادات (فروعي فقط) ────────────────────
    public async Task<IActionResult> Activity()
    {
        var me = await EnsureParentAsync();
        if (me is null)
        {
            TempData["Error"] = "هذه الصفحة للشركة الرئيسية فقط.";
            return RedirectToAction("Index", "Home");
        }

        var branchIds = await _branch.BranchUserIdsAsync(Uid);

        // كل مزايدات فروعي فقط — دون أي رؤية لمزايدات المنافسين
        var bids = await _db.Bids.AsNoTracking()
            .Include(b => b.Bidder)
            .Include(b => b.Auction).ThenInclude(a => a!.Crop)
            .Include(b => b.Auction).ThenInclude(a => a!.Farmer)
            .Where(b => branchIds.Contains(b.BidderId))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        ViewBag.Pending = bids.Where(b => b.Status == BidStatus.PendingApproval).ToList();
        return View(bids);
    }

    // ── موافقة الرئيسية على مزايدة فرع تجاوزت الحد ────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveBid(int bidId)
    {
        var me = await EnsureParentAsync();
        if (me is null) return Forbid();

        var bid = await _db.Bids.Include(b => b.Auction).ThenInclude(a => a!.Bids)
            .FirstOrDefaultAsync(b => b.Id == bidId);
        if (bid is null) return NotFound();

        // يجب أن يكون المزايد فرعاً تابعاً لي
        var branch = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == bid.BidderId && p.ParentCompanyId == Uid);
        if (branch is null) return Forbid();

        if (bid.Status != BidStatus.PendingApproval)
        {
            TempData["Error"] = "هذه المزايدة لم تعد بانتظار الموافقة.";
            return RedirectToAction(nameof(Activity));
        }

        var auction = bid.Auction!;
        if (!auction.IsLive)
        {
            bid.Status = BidStatus.Rejected;
            await _db.SaveChangesAsync();
            TempData["Error"] = "انتهت نافذة المزاد قبل الموافقة، فتعذّر إدخال المزايدة.";
            return RedirectToAction(nameof(Activity));
        }

        bid.ApprovedById = Uid;
        bid.ApprovedAt = DateTime.Now;

        // تدخل المزاد الآن: إن كانت الأعلى تصبح الفائزة وتُنزّل غيرها
        var otherMax = auction.LiveBids.Where(b => b.Id != bid.Id).Select(b => b.UnitPrice).DefaultIfEmpty(0m).Max();
        if (bid.UnitPrice >= otherMax)
        {
            foreach (var b in auction.Bids.Where(b => b.Status == BidStatus.Winning)) b.Status = BidStatus.Submitted;
            bid.Status = BidStatus.Winning;
        }
        else
        {
            bid.Status = BidStatus.Submitted;
        }
        await _db.SaveChangesAsync();

        await _notify.PushAsync(bid.BidderId, "وافقت الرئيسية على مزايدتك",
            $"{auction.Title} — أُدخلت مزايدتك ({bid.UnitPrice:N2} للوحدة).", $"/Auctions/Details/{auction.Id}");
        await _notify.PushAsync(auction.FarmerId, "مزايدة جديدة على مزادك",
            $"{auction.Title} — {bid.UnitPrice:N2} للوحدة.", $"/Auctions/Details/{auction.Id}");

        TempData["Success"] = "تمت الموافقة وأُدخلت مزايدة الفرع في المزاد.";
        return RedirectToAction(nameof(Activity));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectBid(int bidId, string? reason)
    {
        var me = await EnsureParentAsync();
        if (me is null) return Forbid();

        var bid = await _db.Bids.Include(b => b.Auction).FirstOrDefaultAsync(b => b.Id == bidId);
        if (bid is null) return NotFound();

        var branch = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == bid.BidderId && p.ParentCompanyId == Uid);
        if (branch is null) return Forbid();

        if (bid.Status != BidStatus.PendingApproval)
        {
            TempData["Error"] = "هذه المزايدة لم تعد بانتظار الموافقة.";
            return RedirectToAction(nameof(Activity));
        }

        bid.Status = BidStatus.Rejected;
        bid.ApprovedById = Uid;
        bid.ApprovedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        await _notify.PushAsync(bid.BidderId, "رفضت الرئيسية مزايدتك",
            $"{bid.Auction?.Title} — {(string.IsNullOrWhiteSpace(reason) ? "تجاوزت حدّك المصرّح به." : reason)}",
            $"/Auctions/Details/{bid.AuctionId}");

        TempData["Success"] = "تم رفض المزايدة.";
        return RedirectToAction(nameof(Activity));
    }
}
