using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly EmailQueue _emails;
    private readonly IEmailSender _emailSender;

    public AdminController(SawmDbContext db, UserManager<ApplicationUser> users, EmailQueue emails, IEmailSender emailSender)
    {
        _db = db;
        _users = users;
        _emails = emails;
        _emailSender = emailSender;
    }

    /// <summary>طابور اعتماد المزادات — المزادات بانتظار تدقيق الإدارة</summary>
    public async Task<IActionResult> PendingAuctions()
    {
        var list = await _db.Auctions.AsNoTracking()
            .Include(a => a.Crop).Include(a => a.Farmer).Include(a => a.Broker)
            .Where(a => a.Status == AuctionStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.PendingAuctions = await _db.Auctions.CountAsync(a => a.Status == AuctionStatus.Pending);
        ViewBag.Users = await _db.Users.CountAsync();
        ViewBag.Farmers = await _db.Users.CountAsync(u => u.UserType == UserType.Farmer);
        ViewBag.Brokers = await _db.Users.CountAsync(u => u.UserType == UserType.Broker);
        ViewBag.Companies = await _db.Users.CountAsync(u => u.UserType == UserType.Company);
        ViewBag.Auctions = await _db.Auctions.CountAsync();
        ViewBag.Tenders = await _db.Tenders.CountAsync();
        ViewBag.Contracts = await _db.Contracts.CountAsync();
        ViewBag.Disputes = await _db.Contracts.CountAsync(c => c.Status == ContractStatus.Disputed);
        // SQLite لا يدعم SUM على decimal في SQL — نُجمِّع في الذاكرة لضمان عمل المزوّدين معاً
        ViewBag.GMV = (await _db.Contracts.Where(c => c.Status == ContractStatus.Completed)
            .Select(c => c.TotalValue).ToListAsync()).Sum();
        ViewBag.Revenue = (await _db.Contracts.Where(c => c.Status == ContractStatus.Completed)
            .Select(c => c.PlatformCommission).ToListAsync()).Sum();
        ViewBag.EscrowHeld = (await _db.Contracts.Where(c => c.Escrow == EscrowStatus.Held)
            .Select(c => c.TotalValue).ToListAsync()).Sum();

        ViewBag.RecentContracts = await _db.Contracts.AsNoTracking()
            .Include(c => c.Crop).Include(c => c.Seller).Include(c => c.Buyer).Include(c => c.Broker)
            .OrderByDescending(c => c.CreatedAt).Take(10).ToListAsync();

        return View();
    }

    public async Task<IActionResult> Users(UserType? type, string? q)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (type is not null) query = query.Where(u => u.UserType == type);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.FullName.Contains(q) || u.Email!.Contains(q));

        ViewBag.Type = type;
        ViewBag.Q = q;
        return View(await query.OrderByDescending(u => u.CreatedAt).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVerify(string id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.IsVerified = !user.IsVerified;
        await _db.SaveChangesAsync();

        TempData["Success"] = user.IsVerified ? "تم توثيق الحساب." : "تم إلغاء التوثيق.";
        return RedirectToAction(nameof(Users));
    }

    // ── إدارة المحاصيل ─────────────────────────────────────────────────────
    public async Task<IActionResult> Crops() =>
        View(await _db.Crops.AsNoTracking().OrderBy(c => c.Category).ThenBy(c => c.Name).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCrop(int id, string name, string? category, string unit, decimal referencePrice, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "اسم المحصول مطلوب.";
            return RedirectToAction(nameof(Crops));
        }

        if (id > 0)
        {
            var crop = await _db.Crops.FindAsync(id);
            if (crop is null) return NotFound();
            crop.Name = name.Trim();
            crop.Category = category;
            crop.Unit = string.IsNullOrWhiteSpace(unit) ? "كجم" : unit;
            crop.ReferencePrice = referencePrice;
            crop.IsActive = isActive;
        }
        else
        {
            _db.Crops.Add(new Crop
            {
                Name = name.Trim(),
                Category = category,
                Unit = string.IsNullOrWhiteSpace(unit) ? "كجم" : unit,
                ReferencePrice = referencePrice,
                IsActive = isActive
            });
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حفظ المحصول.";
        return RedirectToAction(nameof(Crops));
    }

    /// <summary>حذف محصول من القائمة. المستخدم في سجلات قائمة يُخفى بدل حذفه (لحماية البيانات).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCrop(int id)
    {
        var crop = await _db.Crops.FindAsync(id);
        if (crop is null) return NotFound();

        var used = await _db.Auctions.AnyAsync(a => a.CropId == id)
                || await _db.Tenders.AnyAsync(t => t.CropId == id)
                || await _db.Contracts.AnyAsync(c => c.CropId == id);

        if (used)
        {
            // لا يُحذف نهائياً حتى لا تُيتّم المزادات/المناقصات/العقود المرتبطة — يُخفى من قوائم الاختيار
            crop.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = $"\"{crop.Name}\" مستخدم في سجلات قائمة، فلا يمكن حذفه نهائياً — أُخفي من قوائم الاختيار بدلاً من ذلك.";
        }
        else
        {
            _db.Crops.Remove(crop);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"تم حذف المحصول \"{crop.Name}\".";
        }
        return RedirectToAction(nameof(Crops));
    }

    /// <summary>تقرير أداء: أفضل المزارعين والوسطاء حسب القيمة المتداولة</summary>
    public async Task<IActionResult> Reports()
    {
        var completed = await _db.Contracts.AsNoTracking()
            .Include(c => c.Seller).Include(c => c.Broker).Include(c => c.Crop)
            .Where(c => c.Status == ContractStatus.Completed)
            .ToListAsync();

        ViewBag.TopFarmers = completed
            .GroupBy(c => new { c.SellerId, Name = c.Seller!.FullName })
            .Select(g => new { g.Key.Name, Value = g.Sum(x => x.TotalValue), Deals = g.Count() })
            .OrderByDescending(x => x.Value).Take(10)
            .Select(x => Tuple.Create(x.Name, x.Value, x.Deals)).ToList();

        ViewBag.TopBrokers = completed
            .Where(c => c.Broker != null)
            .GroupBy(c => new { c.BrokerId, Name = c.Broker!.FullName })
            .Select(g => new { g.Key.Name, Value = g.Sum(x => x.BrokerCommission), Deals = g.Count() })
            .OrderByDescending(x => x.Value).Take(10)
            .Select(x => Tuple.Create(x.Name, x.Value, x.Deals)).ToList();

        ViewBag.ByCrop = completed
            .GroupBy(c => new { c.CropId, Name = c.Crop!.Name, c.Crop.Unit })
            .Select(g => new { g.Key.Name, g.Key.Unit, Qty = g.Sum(x => x.Quantity), Value = g.Sum(x => x.TotalValue) })
            .OrderByDescending(x => x.Value)
            .Select(x => Tuple.Create(x.Name, x.Unit, x.Qty, x.Value)).ToList();

        return View();
    }

    // ── مستلمو الإشعارات بالبريد: عناوين إضافية تصلها نسخة من كل إشعارات المنصة ──
    public async Task<IActionResult> NotificationEmails()
    {
        ViewBag.EmailConfigured = _emailSender.IsConfigured;
        var list = await _db.NotificationEmails.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt).ToListAsync();
        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNotificationEmail(string emails, string? label)
    {
        // يقبل بريداً واحداً أو أكثر: سطر لكل بريد أو مفصولة بفاصلة/فاصلة منقوطة/مسافة
        var candidates = (emails ?? "")
            .Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (candidates.Count == 0)
        {
            TempData["Error"] = "أدخل بريداً إلكترونياً واحداً على الأقل.";
            return RedirectToAction(nameof(NotificationEmails));
        }

        var existing = await _db.NotificationEmails.Select(e => e.Email.ToLower()).ToListAsync();
        int added = 0, skipped = 0, invalid = 0;
        foreach (var e in candidates)
        {
            if (!e.Contains('@') || e.Length < 5 || !e.Contains('.')) { invalid++; continue; }
            if (existing.Contains(e)) { skipped++; continue; }
            _db.NotificationEmails.Add(new NotificationEmail { Email = e, Label = label?.Trim() });
            existing.Add(e);
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync();

        var parts = new List<string>();
        if (added > 0) parts.Add($"أُضيف {added} بريد");
        if (skipped > 0) parts.Add($"{skipped} مكرّر (تخطّي)");
        if (invalid > 0) parts.Add($"{invalid} غير صالح");
        if (added > 0) TempData["Success"] = string.Join(" · ", parts);
        else TempData["Error"] = parts.Count > 0 ? string.Join(" · ", parts) : "لم يُضف أي بريد.";

        return RedirectToAction(nameof(NotificationEmails));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleNotificationEmail(int id)
    {
        var e = await _db.NotificationEmails.FindAsync(id);
        if (e is not null)
        {
            e.IsActive = !e.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = e.IsActive ? "تم التفعيل." : "تم الإيقاف.";
        }
        return RedirectToAction(nameof(NotificationEmails));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNotificationEmail(int id)
    {
        var e = await _db.NotificationEmails.FindAsync(id);
        if (e is not null)
        {
            _db.NotificationEmails.Remove(e);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم الحذف.";
        }
        return RedirectToAction(nameof(NotificationEmails));
    }

    /// <summary>إرسال بريد اختبار للتحقّق من إعدادات SMTP</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SendTestEmail(string email)
    {
        email = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            TempData["Error"] = "أدخل بريداً صحيحاً لإرسال الاختبار.";
        else if (!_emailSender.IsConfigured)
            TempData["Error"] = "إعدادات SMTP غير مكتملة — تأكّد من ضبط كلمة المرور في متغيّرات البيئة.";
        else
        {
            _emails.Enqueue(new EmailMessage(new[] { email }, "ساوم — بريد اختبار",
                EmailTemplate.Wrap("بريد اختبار", "إن وصلتك هذه الرسالة فإعدادات البريد تعمل بنجاح. 🎉")));
            TempData["Success"] = $"أُدرج بريد اختبار إلى {email} في الطابور. تحقّق خلال لحظات.";
        }
        return RedirectToAction(nameof(NotificationEmails));
    }
}
