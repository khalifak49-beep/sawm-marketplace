using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

public class HomeController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public HomeController(SawmDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        // الزائر يبدأ من بوابة الدخول (منصة ساوم / الشحن اللوجستي)
        if (User.Identity?.IsAuthenticated != true)
            return View("Gateway");

        var userId = _users.GetUserId(User)!;
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        var vm = new DashboardViewModel
        {
            FullName = user.FullName,
            UserType = user.UserType,
            RoleLabel = Display.UserType(user.UserType),
            RatingAverage = user.RatingAverage,
            UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead)
        };

        vm.ActiveAuctions = await _db.Auctions.CountAsync(a => a.Status == AuctionStatus.Active && a.EndDate > DateTime.Now);
        vm.OpenTenders = await _db.Tenders.CountAsync(t => t.Status == TenderStatus.Open && t.ClosingDate > DateTime.Now);

        var contractsQuery = user.UserType == UserType.Admin
            ? _db.Contracts.AsNoTracking()
            : _db.Contracts.AsNoTracking().Where(c => c.SellerId == userId || c.BuyerId == userId || c.BrokerId == userId);

        vm.ActiveContracts = await contractsQuery.CountAsync(c =>
            c.Status == ContractStatus.Active || c.Status == ContractStatus.ReadyForDelivery || c.Status == ContractStatus.AwaitingSignatures);
        vm.CompletedContracts = await contractsQuery.CountAsync(c => c.Status == ContractStatus.Completed);
        // SQLite لا يدعم SUM على decimal في SQL — نُجمِّع في الذاكرة لضمان عمل المزوّدين معاً
        vm.TotalTradedValue = (await contractsQuery.Where(c => c.Status == ContractStatus.Completed)
            .Select(c => c.TotalValue).ToListAsync()).Sum();
        vm.PendingEscrow = (await contractsQuery.Where(c => c.Escrow == EscrowStatus.Held)
            .Select(c => c.TotalValue).ToListAsync()).Sum();

        vm.MyBids = await _db.Bids.CountAsync(b => b.BidderId == userId);
        vm.MyOffers = await _db.TenderOffers.CountAsync(o => o.SupplierId == userId || o.BrokerId == userId);

        vm.RecentAuctions = await _db.Auctions.AsNoTracking()
            .Include(a => a.Crop).Include(a => a.Farmer).Include(a => a.Bids)
            .Where(a => a.Status == AuctionStatus.Active)
            .OrderByDescending(a => a.CreatedAt).Take(5).ToListAsync();

        vm.RecentTenders = await _db.Tenders.AsNoTracking()
            .Include(t => t.Crop).Include(t => t.Company).Include(t => t.Offers)
            .Where(t => t.Status == TenderStatus.Open)
            .OrderByDescending(t => t.CreatedAt).Take(5).ToListAsync();

        vm.RecentContracts = await contractsQuery
            .Include(c => c.Crop).Include(c => c.Seller).Include(c => c.Buyer)
            .OrderByDescending(c => c.CreatedAt).Take(5).ToListAsync();

        vm.LatestNotifications = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(6).ToListAsync();

        vm.MarketPrices = await BuildMarketPricesAsync();

        return View(vm);
    }

    /// <summary>بوابة منصة ساوم — مدخل السوق الزراعي من شاشة البوابة</summary>
    [HttpGet]
    public Task<IActionResult> Sawm() => LandingAsync();

    /// <summary>الصفحة التعريفية للزوار — نمط "سوق/دليل": البحث هو نداء العمل</summary>
    private async Task<IActionResult> LandingAsync()
    {
        ViewBag.Stats = new Dictionary<string, int>
        {
            ["farmers"] = await _db.Users.CountAsync(u => u.UserType == UserType.Farmer),
            ["brokers"] = await _db.Users.CountAsync(u => u.UserType == UserType.Broker),
            ["companies"] = await _db.Users.CountAsync(u => u.UserType == UserType.Company),
            ["auctions"] = await _db.Auctions.CountAsync(a => a.Status == AuctionStatus.Active && a.EndDate > DateTime.Now)
        };

        ViewBag.Featured = await _db.Auctions.AsNoTracking()
            .Include(a => a.Crop).Include(a => a.Farmer).Include(a => a.Bids)
            .Where(a => a.Status == AuctionStatus.Active && a.EndDate > DateTime.Now)
            .OrderBy(a => a.EndDate)
            .Take(3)
            .ToListAsync();

        var crops = await _db.Crops.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .Take(6)
            .ToListAsync();

        return View("Landing", crops);
    }

    /// <summary>مؤشر أسعار مبسّط: يقارن آخر سعر متداول بالسعر المرجعي لكل محصول</summary>
    private async Task<List<MarketPricePoint>> BuildMarketPricesAsync()
    {
        var crops = await _db.Crops.AsNoTracking().Where(c => c.IsActive).Take(6).ToListAsync();
        var cropIds = crops.Select(c => c.Id).ToList();

        var deals = await _db.Contracts.AsNoTracking()
            .Where(c => cropIds.Contains(c.CropId))
            .Select(c => new { c.CropId, c.UnitPrice, c.CreatedAt })
            .ToListAsync();

        return crops.Select(crop =>
        {
            var cropDeals = deals.Where(d => d.CropId == crop.Id).OrderByDescending(d => d.CreatedAt).ToList();
            return new MarketPricePoint
            {
                CropName = crop.Name,
                Unit = crop.Unit,
                ReferencePrice = crop.ReferencePrice,
                LatestTradedPrice = cropDeals.Count > 0 ? cropDeals[0].UnitPrice : crop.ReferencePrice,
                DealsCount = cropDeals.Count
            };
        }).ToList();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
