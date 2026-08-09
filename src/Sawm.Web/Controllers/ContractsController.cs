using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

[Authorize]
public class ContractsController : Controller
{
    private readonly SawmDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly NotificationService _notify;
    private readonly ContractService _service;
    private readonly BranchService _branch;

    public ContractsController(SawmDbContext db, UserManager<ApplicationUser> users,
        NotificationService notify, ContractService service, BranchService branch)
    {
        _db = db;
        _users = users;
        _notify = notify;
        _service = service;
        _branch = branch;
    }

    private string Uid => _users.GetUserId(User)!;

    public async Task<IActionResult> Index(ContractStatus? status)
    {
        var uid = Uid;
        var query = User.IsInRole(Roles.Admin)
            ? _db.Contracts.AsNoTracking()
            : _db.Contracts.AsNoTracking().Where(c => c.SellerId == uid || c.BuyerId == uid || c.BrokerId == uid);

        if (status is not null) query = query.Where(c => c.Status == status);

        ViewBag.Status = status;

        var list = await query
            .Include(c => c.Crop).Include(c => c.Seller).Include(c => c.Buyer).Include(c => c.Broker)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        // معلومات إخفاء الهوية: الإدارة ترى الكل؛ غيرها لا يرى الطرف المقابل
        ViewBag.Uid = uid;
        ViewBag.IsAdmin = User.IsInRole(Roles.Admin);
        ViewBag.MyBranchIds = await _branch.BranchUserIdsAsync(uid);
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        var contract = await LoadAsync(id);
        if (contract is null) return NotFound();

        var isParentOfBuyer = await IsParentOfBuyerAsync(contract);
        if (!CanView(contract) && !isParentOfBuyer) return Forbid();

        ViewBag.Role = isParentOfBuyer && contract.BuyerId != Uid ? "الشركة الرئيسية للمشتري" : RoleInContract(contract);
        ViewBag.IsParentOfBuyer = isParentOfBuyer;
        ViewBag.MyRating = await _db.Ratings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ContractId == id && r.RaterId == Uid);
        return View(contract);
    }

    /// <summary>هل المستخدم الحالي هو الشركة الرئيسية لفرعٍ مشترٍ في هذا العقد؟</summary>
    private async Task<bool> IsParentOfBuyerAsync(Contract c)
    {
        var buyer = await _branch.ProfileAsync(c.BuyerId);
        return buyer?.ParentCompanyId == Uid;
    }

    // ── التوقيع الرقمي ──────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(int id)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();

        var uid = Uid;
        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == uid);
        // دور الموقّع بدل اسمه — للحفاظ على سرية الهوية بين الطرفين
        var signerRole = contract.SellerId == uid ? "البائع" : "المشتري";

        if (contract.SellerId == uid)
        {
            contract.SellerSigned = true;
        }
        else if (contract.BuyerId == uid)
        {
            // إن كان المشتري فرعاً بلا صلاحية إدارة العقود، فالتوقيع محصور بالشركة الرئيسية
            var buyer = await _branch.ProfileAsync(uid);
            if (buyer is { IsBranch: true } && !buyer.CanManageContracts)
            {
                TempData["Error"] = "توقيع العقود محصور بالشركة الرئيسية. لا تملك صلاحية إدارة العقود.";
                return RedirectToAction(nameof(Details), new { id });
            }
            contract.BuyerSigned = true;
        }
        else
        {
            // الشركة الرئيسية توقّع نيابة عن فرعها المشتري (إشراف)
            var buyerProfile = await _branch.ProfileAsync(contract.BuyerId);
            if (buyerProfile?.ParentCompanyId == uid)
                contract.BuyerSigned = true;
            else
                return Forbid();
        }

        if (contract.SellerSigned && contract.BuyerSigned && contract.Status == ContractStatus.AwaitingSignatures)
        {
            contract.Status = ContractStatus.Active;
            await _db.SaveChangesAsync();
            await _service.LogAsync(id, "اكتمال التوقيع", "وقّع الطرفان — العقد أصبح نشطاً.", uid, me.FullName);
            await _notify.PushManyAsync(Parties(contract), "أصبح العقد نشطاً",
                $"{contract.ContractNumber} — يمكن الآن تمويل حساب الضمان.", $"/Contracts/Details/{id}");
            await _notify.NotifyAdminsAsync("اكتمال توقيع عقد",
                $"وقّع الطرفان العقد {contract.ContractNumber} — أصبح نشطاً.", $"/Contracts/Details/{id}");
        }
        else
        {
            await _db.SaveChangesAsync();
            await _service.LogAsync(id, "توقيع رقمي", $"وقّع {signerRole}.", uid, me.FullName);
            await _notify.NotifyAdminsAsync("توقيع عقد",
                $"وقّع {signerRole} العقد {contract.ContractNumber} — بانتظار الطرف الآخر.", $"/Contracts/Details/{id}");

            // تنبيه الطرف الآخر بأن الدور صار عليه — بدونه يبقى العقد معلّقاً بلا إشارة
            var waitingId = contract.SellerSigned ? contract.BuyerId : contract.SellerId;
            await _notify.PushAsync(waitingId, "بانتظار توقيعك على العقد",
                $"{contract.ContractNumber} — وقّع {signerRole}، ولا يصبح العقد نشطاً إلا بتوقيعك.",
                $"/Contracts/Details/{id}");

            if (!string.IsNullOrEmpty(contract.BrokerId) && contract.BrokerId != uid)
                await _notify.PushAsync(contract.BrokerId, "توقيع جزئي على عقد تشرف عليه",
                    $"{contract.ContractNumber} — وقّع {signerRole}، بانتظار الطرف الآخر.",
                    $"/Contracts/Details/{id}");
        }

        TempData["Success"] = contract.SellerSigned && contract.BuyerSigned
            ? "اكتمل التوقيع من الطرفين — العقد أصبح نشطاً."
            : "تم تسجيل توقيعك. بانتظار توقيع الطرف الآخر.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── الضمان المالي: المشتري يمول، والنظام يحرر بعد الاستلام ──────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FundEscrow(int id)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();
        if (contract.BuyerId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        if (contract.Status != ContractStatus.Active)
        {
            TempData["Error"] = "لا يمكن تمويل الضمان قبل اكتمال توقيع الطرفين.";
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Escrow = EscrowStatus.Held;
        await _db.SaveChangesAsync();

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        await _service.LogAsync(id, "تمويل الضمان", $"احتُجز مبلغ {contract.TotalValue:N2} في حساب الضمان.", Uid, me.FullName);
        await _notify.PushManyAsync(Parties(contract), "تم احتجاز مبلغ العقد",
            $"{contract.ContractNumber} — المزارع يمكنه بدء التنفيذ بأمان.", $"/Contracts/Details/{id}");

        TempData["Success"] = "تم احتجاز المبلغ في حساب الضمان الإلكتروني.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── التحقق الميداني من الجودة: الوسيط أو الإدارة ────────────────────────
    [Authorize(Roles = Roles.Broker + "," + Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Inspect(int id, InspectionResult result, decimal discountPercent, string? notes)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();

        if (contract.BrokerId != Uid && !User.IsInRole(Roles.Admin))
        {
            TempData["Error"] = "التحقق الميداني متاح للوسيط المشرف على العقد فقط.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (contract.Status is not (ContractStatus.Active or ContractStatus.ReadyForDelivery))
        {
            TempData["Error"] = "التحقق متاح للعقود النشطة فقط.";
            return RedirectToAction(nameof(Details), new { id });
        }

        discountPercent = Math.Clamp(discountPercent, 0m, 100m);

        _db.QualityInspections.Add(new QualityInspection
        {
            ContractId = id,
            InspectorId = Uid,
            Result = result,
            DiscountPercent = result == InspectionResult.PassedWithDiscount ? discountPercent : 0m,
            Notes = notes
        });

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);

        switch (result)
        {
            case InspectionResult.Passed:
                contract.Status = ContractStatus.ReadyForDelivery;
                break;

            case InspectionResult.PassedWithDiscount:
                contract.Status = ContractStatus.ReadyForDelivery;
                contract.UnitPrice = Math.Round(contract.UnitPrice * (1 - discountPercent / 100m), 2);
                ContractService.ApplyFinancials(contract);
                break;

            case InspectionResult.Failed:
                contract.Status = ContractStatus.Disputed;
                break;
        }

        await _db.SaveChangesAsync();
        await _service.LogAsync(id, "تحقق ميداني",
            $"النتيجة: {Display.Inspection(result)}{(discountPercent > 0 ? $" — خصم {discountPercent:N1}%" : "")}. {notes}",
            Uid, me.FullName);

        await _notify.PushManyAsync(Parties(contract), "نتيجة التحقق الميداني",
            $"{contract.ContractNumber} — {Display.Inspection(result)}.", $"/Contracts/Details/{id}");

        TempData["Success"] = "تم تسجيل تقرير التحقق.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── تأكيد الاستلام وتحرير الدفع ─────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDelivery(int id)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();
        if (contract.BuyerId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        if (contract.Status is not (ContractStatus.ReadyForDelivery or ContractStatus.Active))
        {
            TempData["Error"] = "لا يمكن تأكيد الاستلام في حالة العقد الحالية.";
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Status = ContractStatus.Delivered;
        await _db.SaveChangesAsync();

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        await _service.LogAsync(id, "تأكيد الاستلام", "أكد المشتري استلام الكمية ومطابقتها.", Uid, me.FullName);
        await _notify.PushManyAsync(Parties(contract), "تم تأكيد الاستلام",
            $"{contract.ContractNumber} — بانتظار تحرير الدفع.", $"/Contracts/Details/{id}");

        TempData["Success"] = "تم تأكيد الاستلام.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleasePayment(int id)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();
        if (contract.BuyerId != Uid && !User.IsInRole(Roles.Admin)) return Forbid();

        if (contract.Status != ContractStatus.Delivered)
        {
            TempData["Error"] = "تحرير الدفع يتم بعد تأكيد الاستلام.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (contract.Escrow != EscrowStatus.Held)
        {
            TempData["Error"] = "لا يوجد مبلغ محتجز لتحريره.";
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Escrow = EscrowStatus.Released;
        contract.Status = ContractStatus.Completed;
        contract.CompletedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        await _service.LogAsync(id, "تحرير الدفع",
            $"حُوِّل صافي {contract.NetToSeller:N2} للمزارع بعد خصم عمولة المنصة {contract.PlatformCommission:N2} وعمولة الوسيط {contract.BrokerCommission:N2}.",
            Uid, me.FullName);

        await _notify.PushAsync(contract.SellerId, "تم تحرير مستحقاتك",
            $"{contract.ContractNumber} — صافي {contract.NetToSeller:N2} ر.ع.", $"/Contracts/Details/{id}");

        if (!string.IsNullOrEmpty(contract.BrokerId))
            await _notify.PushAsync(contract.BrokerId, "تم احتساب عمولتك",
                $"{contract.ContractNumber} — {contract.BrokerCommission:N2} ر.ع.", $"/Contracts/Details/{id}");

        TempData["Success"] = "اكتمل العقد وتم تحرير الدفع.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RaiseDispute(int id, string reason)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();
        if (!CanView(contract)) return Forbid();

        if (contract.Status == ContractStatus.Completed)
        {
            TempData["Error"] = "لا يمكن فتح نزاع على عقد مكتمل.";
            return RedirectToAction(nameof(Details), new { id });
        }

        contract.Status = ContractStatus.Disputed;
        await _db.SaveChangesAsync();

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        await _service.LogAsync(id, "فتح نزاع", reason, Uid, me.FullName);

        var admins = await _db.Users.Where(u => u.UserType == UserType.Admin).Select(u => u.Id).ToListAsync();
        await _notify.PushManyAsync(Parties(contract).Concat(admins), "فتح نزاع على عقد",
            $"{contract.ContractNumber} — {reason}", $"/Contracts/Details/{id}");

        TempData["Success"] = "تم تسجيل النزاع وإحالته للتحكيم الداخلي.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveDispute(int id, bool refundBuyer, string resolution)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();

        if (refundBuyer)
        {
            contract.Escrow = contract.Escrow == EscrowStatus.Held ? EscrowStatus.Refunded : contract.Escrow;
            contract.Status = ContractStatus.Cancelled;
        }
        else
        {
            contract.Status = ContractStatus.ReadyForDelivery;
        }
        await _db.SaveChangesAsync();

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == Uid);
        await _service.LogAsync(id, "قرار تحكيم", resolution, Uid, me.FullName);
        await _notify.PushManyAsync(Parties(contract), "صدر قرار التحكيم",
            $"{contract.ContractNumber} — {resolution}", $"/Contracts/Details/{id}");

        TempData["Success"] = "تم إصدار قرار التحكيم.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── التقييم المتبادل بعد الإتمام ────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rate(int id, int timeliness, int qualityConsistency, int communication, string? comment)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound();
        if (!CanView(contract)) return Forbid();

        if (contract.Status != ContractStatus.Completed)
        {
            TempData["Error"] = "التقييم متاح بعد اكتمال العقد.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var uid = Uid;
        var ratedId = uid == contract.SellerId ? contract.BuyerId
            : uid == contract.BuyerId ? contract.SellerId
            : contract.SellerId;

        if (await _db.Ratings.AnyAsync(r => r.ContractId == id && r.RaterId == uid))
        {
            TempData["Error"] = "سبق أن قيّمت هذا العقد.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var rating = new Rating
        {
            ContractId = id,
            RaterId = uid,
            RatedUserId = ratedId,
            Timeliness = Math.Clamp(timeliness, 1, 5),
            QualityConsistency = Math.Clamp(qualityConsistency, 1, 5),
            Communication = Math.Clamp(communication, 1, 5),
            Comment = comment
        };
        _db.Ratings.Add(rating);
        await _db.SaveChangesAsync();

        // تحديث متوسط التقييم التراكمي للطرف المُقيَّم
        var rated = await _db.Users.FirstAsync(u => u.Id == ratedId);
        var total = rated.RatingAverage * rated.RatingCount + rating.Score;
        rated.RatingCount += 1;
        rated.RatingAverage = Math.Round(total / rated.RatingCount, 2);
        await _db.SaveChangesAsync();

        await _notify.PushAsync(ratedId, "تلقّيت تقييماً جديداً",
            $"{contract.ContractNumber} — {rating.Score:N1}/5.", $"/Contracts/Details/{id}");

        TempData["Success"] = "شكراً، تم تسجيل تقييمك.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── مساعدات ────────────────────────────────────────────────────────────
    private Task<Contract?> LoadAsync(int id, bool tracking = false)
    {
        var q = _db.Contracts
            .Include(c => c.Crop)
            .Include(c => c.Seller)
            .Include(c => c.Buyer)
            .Include(c => c.Broker)
            .Include(c => c.Events)
            .Include(c => c.Inspections).ThenInclude(i => i.Inspector)
            .AsQueryable();

        if (!tracking) q = q.AsNoTracking();
        return q.FirstOrDefaultAsync(c => c.Id == id);
    }

    private bool CanView(Contract c) =>
        User.IsInRole(Roles.Admin) || c.SellerId == Uid || c.BuyerId == Uid || c.BrokerId == Uid;

    private string RoleInContract(Contract c)
    {
        var uid = Uid;
        if (c.SellerId == uid) return "بائع";
        if (c.BuyerId == uid) return "مشترٍ";
        if (c.BrokerId == uid) return "وسيط";
        return "إدارة";
    }

    private static IEnumerable<string> Parties(Contract c) =>
        new[] { c.SellerId, c.BuyerId, c.BrokerId ?? "" }.Where(s => !string.IsNullOrEmpty(s));
}
