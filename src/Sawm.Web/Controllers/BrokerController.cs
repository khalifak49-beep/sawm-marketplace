using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>مساحة عمل الوسيط: اعتماد المزادات، متابعة العمولات، وربط المزارع بالشركة</summary>
[Authorize(Roles = Roles.Broker + "," + Roles.Admin)]
public class BrokerController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public BrokerController(SawmDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private string Uid => _users.GetUserId(User)!;

    public async Task<IActionResult> Workspace()
    {
        var uid = Uid;
        var profile = await _db.BrokerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == uid);

        var vm = new BrokerWorkspaceViewModel
        {
            CommissionRate = profile?.CommissionRate ?? 0m,

            // مزادات متاحة للتبنّي: بانتظار الإدارة وبلا وسيط مشرف بعد
            PendingApproval = await _db.Auctions.AsNoTracking()
                .Include(a => a.Crop).Include(a => a.Farmer)
                .Where(a => a.Status == AuctionStatus.Pending && a.BrokerId == null)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(),

            ManagedAuctions = await _db.Auctions.AsNoTracking()
                .Include(a => a.Crop).Include(a => a.Farmer).Include(a => a.Bids)
                .Where(a => a.BrokerId == uid)
                .OrderByDescending(a => a.CreatedAt).Take(20)
                .ToListAsync(),

            OpenTenders = await _db.Tenders.AsNoTracking()
                .Include(t => t.Crop).Include(t => t.Company).Include(t => t.Offers)
                .Where(t => t.Status == TenderStatus.Open && t.ClosingDate > DateTime.Now)
                .OrderBy(t => t.ClosingDate).Take(20)
                .ToListAsync(),

            BrokeredContracts = await _db.Contracts.AsNoTracking()
                .Include(c => c.Crop).Include(c => c.Seller).Include(c => c.Buyer)
                .Where(c => c.BrokerId == uid)
                .OrderByDescending(c => c.CreatedAt).Take(20)
                .ToListAsync()
        };

        // SQLite لا يدعم SUM على decimal في SQL — نُجمِّع في الذاكرة لضمان عمل المزوّدين معاً
        vm.EarnedCommission = (await _db.Contracts
            .Where(c => c.BrokerId == uid && c.Status == ContractStatus.Completed)
            .Select(c => c.BrokerCommission).ToListAsync()).Sum();

        vm.PipelineCommission = (await _db.Contracts
            .Where(c => c.BrokerId == uid && c.Status != ContractStatus.Completed && c.Status != ContractStatus.Cancelled)
            .Select(c => c.BrokerCommission).ToListAsync()).Sum();

        return View(vm);
    }

    /// <summary>دليل المزارعين المتاحين للتمثيل</summary>
    public async Task<IActionResult> Farmers(string? region, string? q)
    {
        var query = _db.Users.AsNoTracking()
            .Include(u => u.FarmerProfile)
            .Where(u => u.UserType == UserType.Farmer);

        if (!string.IsNullOrWhiteSpace(region)) query = query.Where(u => u.Region == region);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => u.FullName.Contains(q));

        ViewBag.Regions = await _db.Users.Where(u => u.UserType == UserType.Farmer && u.Region != null)
            .Select(u => u.Region!).Distinct().ToListAsync();
        ViewBag.Region = region;
        ViewBag.Q = q;

        var farmers = await query.OrderByDescending(u => u.RatingAverage).ToListAsync();

        // عدد المزادات النشطة لكل مزارع
        var ids = farmers.Select(f => f.Id).ToList();
        ViewBag.ActiveCounts = await _db.Auctions
            .Where(a => ids.Contains(a.FarmerId) && a.Status == AuctionStatus.Active)
            .GroupBy(a => a.FarmerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return View(farmers);
    }

    /// <summary>دليل الشركات المشترية</summary>
    public async Task<IActionResult> Companies(string? q)
    {
        var query = _db.Users.AsNoTracking()
            .Include(u => u.CompanyProfile)
            .Where(u => u.UserType == UserType.Company);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.FullName.Contains(q) || u.CompanyProfile!.CompanyName.Contains(q));

        ViewBag.Q = q;
        var companies = await query.OrderByDescending(u => u.RatingAverage).ToListAsync();

        var ids = companies.Select(c => c.Id).ToList();
        ViewBag.OpenTenderCounts = await _db.Tenders
            .Where(t => ids.Contains(t.CompanyId) && t.Status == TenderStatus.Open)
            .GroupBy(t => t.CompanyId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return View(companies);
    }
}
