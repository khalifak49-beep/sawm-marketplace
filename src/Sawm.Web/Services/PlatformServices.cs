using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;

namespace Sawm.Web.Services;

/// <summary>أسماء الأدوار الثابتة</summary>
public static class Roles
{
    public const string Farmer = "Farmer";
    public const string Broker = "Broker";
    public const string Company = "Company";
    public const string Admin = "Admin";

    public static string For(UserType t) => t switch
    {
        UserType.Farmer => Farmer,
        UserType.Broker => Broker,
        UserType.Company => Company,
        _ => Admin
    };

    public static string Arabic(string role) => role switch
    {
        Farmer => "مزارع",
        Broker => "وسيط",
        Company => "شركة",
        Admin => "إدارة",
        _ => role
    };
}

/// <summary>منطق الفروع: الصلاحيات وحدود المزايدة وعلاقة الفرع بالشركة الرئيسية</summary>
public class BranchService
{
    private readonly SawmDbContext _db;
    public BranchService(SawmDbContext db) => _db = db;

    /// <summary>ملف الشركة (يتضمن معلومات الفرع إن كان فرعاً)</summary>
    public Task<CompanyProfile?> ProfileAsync(string userId) =>
        _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);

    /// <summary>هل تتجاوز هذه المزايدة حدَّ الفرع فتحتاج موافقة الرئيسية؟</summary>
    public static bool NeedsApproval(CompanyProfile? p, decimal totalValue) =>
        p is { IsBranch: true, BidLimit: > 0 } && totalValue > p.BidLimit;

    /// <summary>فروع شركة رئيسية معيّنة</summary>
    public Task<List<CompanyProfile>> BranchesAsync(string parentUserId) =>
        _db.CompanyProfiles.AsNoTracking().Include(p => p.User)
            .Where(p => p.ParentCompanyId == parentUserId)
            .OrderBy(p => p.CompanyName)
            .ToListAsync();

    /// <summary>معرّفات مستخدمي الفروع التابعة لشركة رئيسية</summary>
    public Task<List<string>> BranchUserIdsAsync(string parentUserId) =>
        _db.CompanyProfiles.Where(p => p.ParentCompanyId == parentUserId)
            .Select(p => p.UserId).ToListAsync();
}

/// <summary>إشعارات داخلية + بريد إلكتروني: كل إشعار يُحفظ في المنصة ويُرسل بريدياً للمعنيّ ولمستلمي المتابعة</summary>
public class NotificationService
{
    private readonly SawmDbContext _db;
    private readonly EmailQueue _emails;
    private readonly EmailSettings _emailSettings;

    public NotificationService(SawmDbContext db, EmailQueue emails, Microsoft.Extensions.Options.IOptions<EmailSettings> emailSettings)
    {
        _db = db;
        _emails = emails;
        _emailSettings = emailSettings.Value;
    }

    public async Task PushAsync(string userId, string title, string? body = null, string? url = null)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title.Length > 150 ? title[..150] : title,
            Body = body,
            Url = url
        });
        await _db.SaveChangesAsync();
        await EnqueueEmailsAsync(new[] { userId }, title, body, url);
    }

    public async Task PushManyAsync(IEnumerable<string> userIds, string title, string? body = null, string? url = null)
    {
        var ids = userIds.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        if (ids.Count == 0) return;
        _db.Notifications.AddRange(ids.Select(id => new Notification
        {
            UserId = id,
            Title = title.Length > 150 ? title[..150] : title,
            Body = body,
            Url = url
        }));
        await _db.SaveChangesAsync();
        await EnqueueEmailsAsync(ids, title, body, url);
    }

    public Task<int> UnreadCountAsync(string userId) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    /// <summary>
    /// يُدرج رسائل البريد في الطابور الخلفي: رسالة فردية لكل مستخدم (منعاً لتسرّب العناوين بين الأطراف)
    /// ونسخة واحدة لمستلمي المتابعة الإضافيين.
    /// </summary>
    private async Task EnqueueEmailsAsync(IReadOnlyList<string> userIds, string title, string? body, string? url)
    {
        try
        {
            var recipients = await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id) && u.Email != null)
                .Select(u => new { u.Email, u.FullName })
                .ToListAsync();

            var subscribers = await _db.NotificationEmails.AsNoTracking()
                .Where(e => e.IsActive)
                .Select(e => e.Email)
                .ToListAsync();

            var actionUrl = BuildAbsoluteUrl(url);
            var html = EmailTemplate.Wrap(title, FormatBody(body), actionUrl, actionUrl is null ? null : "فتح في المنصة");
            var subject = $"ساوم — {title}";

            foreach (var r in recipients)
                if (!string.IsNullOrWhiteSpace(r.Email))
                    _emails.Enqueue(new EmailMessage(new[] { r.Email! }, subject, html));

            if (subscribers.Count > 0)
                _emails.Enqueue(new EmailMessage(Array.Empty<string>(), $"[متابعة] {subject}", html, subscribers));
        }
        catch
        {
            // البريد مكمّل للإشعار الداخلي — أي خطأ فيه يجب ألّا يُعطّل الإجراء الأساسي
        }
    }

    private string? BuildAbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        var baseUrl = _emailSettings.BaseUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl}/{url.TrimStart('/')}";
    }

    private static string FormatBody(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "لديك تحديث جديد على المنصة." : System.Net.WebUtility.HtmlEncode(body);
}

/// <summary>
/// محرك المطابقة: يحسب درجة 0-100 لعرض على مناقصة، بوزن السعر والكمية
/// وسجل المورد والقرب الجغرافي وسرعة التسليم.
/// </summary>
public class MatchingService
{
    public decimal ScoreOffer(Tender tender, TenderOffer offer, ApplicationUser? supplier)
    {
        decimal score = 0;

        // 1) السعر — 40 نقطة: كلما اقترب من الصفر مقابل السقف زادت النقاط
        if (tender.MaxUnitPrice > 0)
        {
            var ratio = offer.UnitPrice / tender.MaxUnitPrice;
            var priceScore = ratio >= 1m ? 0m : (1m - ratio) * 40m + 10m; // عرض تحت السقف يبدأ من 10
            score += Math.Clamp(priceScore, 0m, 40m);
        }
        else
        {
            score += 20m;
        }

        // 2) تغطية الكمية — 25 نقطة
        if (tender.Quantity > 0)
        {
            var coverage = Math.Min(offer.AvailableQuantity / tender.Quantity, 1m);
            score += coverage * 25m;
        }

        // 3) سجل المورد — 20 نقطة
        if (supplier is not null)
        {
            var rep = supplier.RatingCount > 0 ? supplier.RatingAverage / 5m : 0.6m; // افتراضي للمستخدم الجديد
            score += rep * 15m;
            if (supplier.IsVerified) score += 5m;
        }

        // 4) الموعد — 15 نقطة
        if (offer.EarliestDelivery is DateTime d)
        {
            score += d <= tender.DeliveryDate ? 15m : 5m;
        }
        else
        {
            score += 7m;
        }

        return Math.Round(Math.Clamp(score, 0m, 100m), 2);
    }

    /// <summary>ترتيب مزايدات المزاد: الأعلى سعراً أولاً ثم الأقدم تقديماً</summary>
    public IEnumerable<Bid> Rank(IEnumerable<Bid> bids) =>
        bids.OrderByDescending(b => b.UnitPrice).ThenBy(b => b.CreatedAt);
}

/// <summary>توليد العقود الرقمية، حساب العمولات، وسجل التدقيق</summary>
public class ContractService
{
    private readonly SawmDbContext _db;
    private readonly NotificationService _notify;

    public const decimal DefaultPlatformCommission = 2.0m;

    public ContractService(SawmDbContext db, NotificationService notify)
    {
        _db = db;
        _notify = notify;
    }

    public async Task<string> NextContractNumberAsync()
    {
        var year = DateTime.Now.Year;
        var prefix = $"SW-{year}-";
        var count = await _db.Contracts.CountAsync(c => c.ContractNumber.StartsWith(prefix));
        return $"{prefix}{(count + 1):D5}";
    }

    /// <summary>يحسب العمولات وصافي مستحق البائع بناءً على القيمة الإجمالية</summary>
    public static void ApplyFinancials(Contract c)
    {
        c.TotalValue = Math.Round(c.Quantity * c.UnitPrice, 2);
        c.PlatformCommission = Math.Round(c.TotalValue * c.PlatformCommissionRate / 100m, 2);
        c.BrokerCommission = Math.Round(c.TotalValue * c.BrokerCommissionRate / 100m, 2);
        c.NetToSeller = Math.Round(c.TotalValue - c.PlatformCommission - c.BrokerCommission, 2);
    }

    public async Task LogAsync(int contractId, string action, string? details, string? actorId, string? actorName)
    {
        _db.ContractEvents.Add(new ContractEvent
        {
            ContractId = contractId,
            Action = action,
            Details = details,
            ActorId = actorId,
            ActorName = actorName
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>ترسية مزاد: تحويل المزايدة الفائزة إلى عقد رقمي</summary>
    public async Task<Contract> CreateFromAuctionAsync(Auction auction, Bid winning, string? actorId, string? actorName)
    {
        decimal brokerRate = 0m;
        if (!string.IsNullOrEmpty(auction.BrokerId))
        {
            var bp = await _db.BrokerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == auction.BrokerId);
            brokerRate = bp?.CommissionRate ?? 0m;
        }

        var contract = new Contract
        {
            ContractNumber = await NextContractNumberAsync(),
            SellerId = auction.FarmerId,
            BuyerId = winning.BidderId,
            BrokerId = auction.BrokerId,
            CropId = auction.CropId,
            AuctionId = auction.Id,
            Quantity = auction.Quantity,
            UnitPrice = winning.UnitPrice,
            PlatformCommissionRate = DefaultPlatformCommission,
            BrokerCommissionRate = brokerRate,
            DeliveryDate = auction.ExpectedHarvestDate ?? auction.EndDate.AddDays(7),
            DeliveryLocation = auction.PickupLocation,
            Logistics = auction.Logistics,
            Terms = BuildTerms(auction.Description, auction.Quality)
        };
        ApplyFinancials(contract);

        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync();

        await LogAsync(contract.Id, "إنشاء العقد", $"ترسية المزاد #{auction.Id} على مزايدة #{winning.Id}", actorId, actorName);

        await _notify.PushManyAsync(
            new[] { contract.SellerId, contract.BuyerId, contract.BrokerId ?? "" },
            $"تم إنشاء العقد {contract.ContractNumber}",
            $"القيمة الإجمالية {contract.TotalValue:N2} ر.ع — بانتظار التوقيع الرقمي من الطرفين.",
            $"/Contracts/Details/{contract.Id}");

        return contract;
    }

    /// <summary>ترسية مناقصة: تحويل العرض الفائز إلى عقد رقمي</summary>
    public async Task<Contract> CreateFromTenderAsync(Tender tender, TenderOffer winning, string? actorId, string? actorName)
    {
        decimal brokerRate = 0m;
        if (!string.IsNullOrEmpty(winning.BrokerId))
        {
            var bp = await _db.BrokerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == winning.BrokerId);
            brokerRate = bp?.CommissionRate ?? 0m;
        }

        var contract = new Contract
        {
            ContractNumber = await NextContractNumberAsync(),
            SellerId = winning.SupplierId,
            BuyerId = tender.CompanyId,
            BrokerId = winning.BrokerId,
            CropId = tender.CropId,
            TenderId = tender.Id,
            Quantity = Math.Min(winning.AvailableQuantity, tender.Quantity),
            UnitPrice = winning.UnitPrice,
            PlatformCommissionRate = DefaultPlatformCommission,
            BrokerCommissionRate = brokerRate,
            DeliveryDate = tender.DeliveryDate,
            DeliveryLocation = tender.DeliveryLocation,
            Logistics = tender.Logistics,
            Terms = BuildTerms(tender.Specifications, tender.RequiredQuality)
        };
        ApplyFinancials(contract);

        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync();

        await LogAsync(contract.Id, "إنشاء العقد", $"ترسية المناقصة #{tender.Id} على العرض #{winning.Id}", actorId, actorName);

        await _notify.PushManyAsync(
            new[] { contract.SellerId, contract.BuyerId, contract.BrokerId ?? "" },
            $"تم إنشاء العقد {contract.ContractNumber}",
            $"القيمة الإجمالية {contract.TotalValue:N2} ر.ع — بانتظار التوقيع الرقمي من الطرفين.",
            $"/Contracts/Details/{contract.Id}");

        return contract;
    }

    private static string BuildTerms(string? specs, QualityGrade quality) =>
        $"""
        1) يلتزم البائع بتوريد الكمية المتفق عليها بدرجة جودة: {Display.Quality(quality)}.
        2) يُحتجز مبلغ العقد في حساب ضمان إلكتروني ولا يُحرَّر إلا بعد تأكيد الاستلام والمطابقة.
        3) يُنفَّذ تحقق ميداني من الجودة قبل التسليم؛ الانحراف الطفيف يعالَج بخصم متدرج متفق عليه.
        4) مسؤولية النقل والتخزين والتأمين تقع على الطرف المحدد في بند اللوجستيات — المنصة ليست طرفاً لوجستياً.
        5) التأخير غير المبرر عن موعد التسليم يخصم من سجل الموثوقية ويخضع لآلية التحكيم الداخلي.
        6) المواصفات المعتمدة: {(string.IsNullOrWhiteSpace(specs) ? "حسب المواصفة القياسية للمحصول" : specs)}
        """;
}

/// <summary>ترجمة قيم التعدادات لنصوص عربية للعرض</summary>
public static class Display
{
    public static string Quality(QualityGrade q) => q switch
    {
        QualityGrade.Premium => "ممتاز",
        QualityGrade.GradeA => "درجة أولى",
        QualityGrade.GradeB => "درجة ثانية",
        _ => "درجة ثالثة"
    };

    public static string AuctionStatus(AuctionStatus s) => s switch
    {
        Models.AuctionStatus.Draft => "مسودة",
        Models.AuctionStatus.Pending => "بانتظار اعتماد الإدارة",
        Models.AuctionStatus.Active => "نشط",
        Models.AuctionStatus.Closed => "مُرسى",
        Models.AuctionStatus.Cancelled => "ملغى",
        _ => "منتهٍ"
    };

    public static string AuctionStatusCss(AuctionStatus s) => s switch
    {
        Models.AuctionStatus.Active => "success",
        Models.AuctionStatus.Pending => "warning",
        Models.AuctionStatus.Closed => "primary",
        Models.AuctionStatus.Cancelled => "danger",
        _ => "secondary"
    };

    public static string AuctionType(AuctionType t) => t == Models.AuctionType.Instant ? "لحظي" : "مستقبلي (مبكر)";

    public static string TenderStatus(TenderStatus s) => s switch
    {
        Models.TenderStatus.Open => "مفتوحة",
        Models.TenderStatus.UnderReview => "قيد التقييم",
        Models.TenderStatus.Awarded => "تمت الترسية",
        Models.TenderStatus.Cancelled => "ملغاة",
        _ => "منتهية"
    };

    public static string TenderStatusCss(TenderStatus s) => s switch
    {
        Models.TenderStatus.Open => "success",
        Models.TenderStatus.UnderReview => "warning",
        Models.TenderStatus.Awarded => "primary",
        _ => "secondary"
    };

    public static string OfferStatus(OfferStatus s) => s switch
    {
        Models.OfferStatus.Submitted => "مقدَّم",
        Models.OfferStatus.Shortlisted => "قائمة مختصرة",
        Models.OfferStatus.Awarded => "فائز",
        _ => "مرفوض"
    };

    public static string BidStatus(BidStatus s) => s switch
    {
        Models.BidStatus.Submitted => "مقدَّمة",
        Models.BidStatus.Winning => "الأعلى",
        Models.BidStatus.Accepted => "مقبولة",
        Models.BidStatus.Rejected => "مرفوضة",
        Models.BidStatus.PendingApproval => "بانتظار موافقة الرئيسية",
        _ => "مسحوبة"
    };

    public static string BidStatusCss(BidStatus s) => s switch
    {
        Models.BidStatus.Winning => "success",
        Models.BidStatus.Accepted => "primary",
        Models.BidStatus.Rejected => "danger",
        Models.BidStatus.PendingApproval => "warning",
        _ => "secondary"
    };

    public static string ContractStatus(ContractStatus s) => s switch
    {
        Models.ContractStatus.AwaitingSignatures => "بانتظار التوقيع",
        Models.ContractStatus.Active => "نشط",
        Models.ContractStatus.ReadyForDelivery => "جاهز للتسليم",
        Models.ContractStatus.Delivered => "تم التسليم",
        Models.ContractStatus.Completed => "مكتمل",
        Models.ContractStatus.Disputed => "نزاع",
        _ => "ملغى"
    };

    public static string ContractStatusCss(ContractStatus s) => s switch
    {
        Models.ContractStatus.Completed => "success",
        Models.ContractStatus.Active or Models.ContractStatus.ReadyForDelivery => "primary",
        Models.ContractStatus.AwaitingSignatures => "warning",
        Models.ContractStatus.Disputed => "danger",
        Models.ContractStatus.Delivered => "info",
        _ => "secondary"
    };

    public static string Escrow(EscrowStatus s) => s switch
    {
        EscrowStatus.NotFunded => "غير مموَّل",
        EscrowStatus.Held => "محتجز",
        EscrowStatus.Released => "محرَّر",
        _ => "مُعاد"
    };

    public static string Logistics(LogisticsResponsibility l) => l switch
    {
        LogisticsResponsibility.Seller => "على البائع (المزارع)",
        LogisticsResponsibility.Buyer => "على المشتري",
        LogisticsResponsibility.Broker => "ينظمها الوسيط",
        _ => "مزود خدمة خارجي"
    };

    public static string UserType(UserType t) => t switch
    {
        Models.UserType.Farmer => "مزارع",
        Models.UserType.Broker => "وسيط",
        Models.UserType.Company => "شركة",
        _ => "إدارة"
    };

    public static string Inspection(InspectionResult r) => r switch
    {
        InspectionResult.Pending => "قيد التنفيذ",
        InspectionResult.Passed => "مطابق",
        InspectionResult.PassedWithDiscount => "مطابق مع خصم",
        _ => "غير مطابق"
    };

    public static string LogisticsStatus(LogisticsRequestStatus s) => s switch
    {
        LogisticsRequestStatus.Open => "مفتوح",
        LogisticsRequestStatus.Awarded => "مُرسى",
        LogisticsRequestStatus.Completed => "مكتمل",
        _ => "ملغى"
    };
}
