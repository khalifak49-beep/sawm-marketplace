using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Sawm.Web.Models;

/// <summary>مستخدم المنصة — يمتد من هوية ASP.NET</summary>
public class ApplicationUser : IdentityUser
{
    [Required, MaxLength(120), Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "نوع الحساب")]
    public UserType UserType { get; set; }

    [MaxLength(80), Display(Name = "المحافظة / المنطقة")]
    public string? Region { get; set; }

    [MaxLength(160), Display(Name = "العنوان")]
    public string? Address { get; set; }

    [Display(Name = "موثّق")]
    public bool IsVerified { get; set; }

    [Display(Name = "تاريخ التسجيل")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>متوسط التقييم المتراكم (0-5)</summary>
    [Column(TypeName = "decimal(3,2)")]
    [Display(Name = "التقييم")]
    public decimal RatingAverage { get; set; }

    [Display(Name = "عدد التقييمات")]
    public int RatingCount { get; set; }

    // ملفات تعريفية حسب النوع
    public FarmerProfile? FarmerProfile { get; set; }
    public BrokerProfile? BrokerProfile { get; set; }
    public CompanyProfile? CompanyProfile { get; set; }
}

/// <summary>الملف الزراعي للمزارع</summary>
public class FarmerProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Display(Name = "المساحة الزراعية (فدان)")]
    [Range(0, 100000)]
    [Column(TypeName = "decimal(10,2)")]
    public decimal FarmArea { get; set; }

    [MaxLength(60), Display(Name = "نوع التربة")]
    public string? SoilType { get; set; }

    [MaxLength(60), Display(Name = "مصدر الري")]
    public string? IrrigationSource { get; set; }

    [MaxLength(200), Display(Name = "الموقع التفصيلي للمزرعة")]
    public string? FarmLocation { get; set; }

    [Display(Name = "سنوات الخبرة")]
    [Range(0, 80)]
    public int ExperienceYears { get; set; }
}

/// <summary>الملف المهني للوسيط</summary>
public class BrokerProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [MaxLength(60), Display(Name = "رقم الترخيص")]
    public string? LicenseNumber { get; set; }

    [Display(Name = "نسبة العمولة %")]
    [Range(0, 30)]
    [Column(TypeName = "decimal(5,2)")]
    public decimal CommissionRate { get; set; } = 2.5m;

    [MaxLength(200), Display(Name = "نطاق التغطية")]
    public string? CoverageArea { get; set; }

    [Display(Name = "عدد الصفقات المنجزة")]
    public int ClosedDeals { get; set; }
}

/// <summary>الملف المؤسسي للشركة المشترية</summary>
public class CompanyProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(150), Display(Name = "اسم الشركة")]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(60), Display(Name = "السجل التجاري")]
    public string? CommercialRegistry { get; set; }

    [MaxLength(80), Display(Name = "طبيعة النشاط")]
    public string? ActivityType { get; set; }

    [Display(Name = "حجم الطلب الشهري المتوقع (طن)")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal MonthlyDemand { get; set; }

    // ── نظام الفروع ─────────────────────────────────────────────
    /// <summary>الشركة الرئيسية التي يتبعها هذا الفرع. null = شركة رئيسية/مستقلة.</summary>
    public string? ParentCompanyId { get; set; }
    public ApplicationUser? ParentCompany { get; set; }

    [Display(Name = "يزايد في المزادات")]
    public bool CanBid { get; set; } = true;

    [Display(Name = "يقدّم عروضاً على المناقصات")]
    public bool CanSubmitOffers { get; set; } = true;

    [Display(Name = "يطرح مناقصات")]
    public bool CanCreateTenders { get; set; }

    [Display(Name = "يوقّع ويدير العقود")]
    public bool CanManageContracts { get; set; }

    /// <summary>سقف القيمة الإجمالية للمزايدة (سعر × كمية) دون موافقة الرئيسية. صفر أو أقل = بلا حد.</summary>
    [Display(Name = "حد المزايدة (القيمة الإجمالية)")]
    [Column(TypeName = "decimal(14,2)")]
    public decimal BidLimit { get; set; }

    [NotMapped] public bool IsBranch => ParentCompanyId != null;
}

/// <summary>المحصول الزراعي</summary>
public class Crop
{
    public int Id { get; set; }

    [Required, MaxLength(80), Display(Name = "اسم المحصول")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(60), Display(Name = "التصنيف")]
    public string? Category { get; set; }

    [MaxLength(20), Display(Name = "وحدة القياس")]
    public string Unit { get; set; } = "كجم";

    [Display(Name = "متوسط السعر المرجعي")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal ReferencePrice { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    public ICollection<Auction> Auctions { get; set; } = new List<Auction>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
}

/// <summary>مزاد يعرضه المزارع (لحظي أو مستقبلي) ويشرف عليه وسيط</summary>
public class Auction
{
    public int Id { get; set; }

    [Required, MaxLength(150), Display(Name = "عنوان المزاد")]
    public string Title { get; set; } = string.Empty;

    [Required] public string FarmerId { get; set; } = string.Empty;
    public ApplicationUser? Farmer { get; set; }

    /// <summary>الوسيط المشرف — اختياري حتى يُسند</summary>
    public string? BrokerId { get; set; }
    public ApplicationUser? Broker { get; set; }

    [Display(Name = "المحصول")]
    public int CropId { get; set; }
    public Crop? Crop { get; set; }

    [Display(Name = "الكمية (بالطن)")]
    [Range(0.01, 1000000)]
    [Column(TypeName = "decimal(12,2)")]
    public decimal Quantity { get; set; }

    /// <summary>وحدة كمية المزاد. المزادات الجديدة بالطن؛ الصفوف القديمة (null) تُعرض بوحدة المحصول.</summary>
    [MaxLength(20)]
    public string? QuantityUnit { get; set; }

    /// <summary>الوحدة الظاهرة للعرض: وحدة المزاد إن وُجدت، وإلا وحدة المحصول.</summary>
    [NotMapped] public string DisplayUnit => QuantityUnit ?? Crop?.Unit ?? "طن";

    [Display(Name = "درجة الجودة")]
    public QualityGrade Quality { get; set; } = QualityGrade.GradeA;

    [Display(Name = "سعر الافتتاح للطن")]
    [Range(0.01, 10000000)]
    [Column(TypeName = "decimal(12,2)")]
    public decimal StartPrice { get; set; }

    [Display(Name = "الحد الأدنى لزيادة المزايدة")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal MinIncrement { get; set; } = 1m;

    [Display(Name = "نوع المزاد")]
    public AuctionType Type { get; set; } = AuctionType.Instant;

    [Display(Name = "الحالة")]
    public AuctionStatus Status { get; set; } = AuctionStatus.Pending;

    [Display(Name = "تاريخ البدء")]
    public DateTime StartDate { get; set; } = DateTime.Now;

    [Display(Name = "تاريخ الإغلاق")]
    public DateTime EndDate { get; set; } = DateTime.Now.AddDays(7);

    [Display(Name = "تاريخ الحصاد المتوقع")]
    public DateTime? ExpectedHarvestDate { get; set; }

    [MaxLength(120), Display(Name = "موقع الاستلام")]
    public string? PickupLocation { get; set; }

    [MaxLength(1000), Display(Name = "الوصف والمواصفات")]
    public string? Description { get; set; }

    [Display(Name = "مسؤولية اللوجستيات")]
    public LogisticsResponsibility Logistics { get; set; } = LogisticsResponsibility.Buyer;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Bid> Bids { get; set; } = new List<Bid>();

    // خصائص محسوبة (غير معيّنة في قاعدة البيانات)
    /// <summary>المزايدات الفاعلة فقط — تستبعد المعلّقة بانتظار الموافقة والمرفوضة والمسحوبة.</summary>
    [NotMapped]
    public IEnumerable<Bid> LiveBids =>
        Bids.Where(b => b.Status is BidStatus.Submitted or BidStatus.Winning or BidStatus.Accepted);

    [NotMapped]
    public decimal CurrentPrice =>
        LiveBids.Any() ? LiveBids.Max(b => b.UnitPrice) : StartPrice;

    [NotMapped] public bool IsLive => Status == AuctionStatus.Active && EndDate > DateTime.Now;
    [NotMapped] public decimal EstimatedValue => CurrentPrice * Quantity;
}

/// <summary>مزايدة على مزاد — يقدمها وسيط أو شركة</summary>
public class Bid
{
    public int Id { get; set; }

    public int AuctionId { get; set; }
    public Auction? Auction { get; set; }

    [Required] public string BidderId { get; set; } = string.Empty;
    public ApplicationUser? Bidder { get; set; }

    [Display(Name = "سعر الوحدة")]
    [Range(0.01, 10000000)]
    [Column(TypeName = "decimal(12,2)")]
    public decimal UnitPrice { get; set; }

    [MaxLength(400), Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public BidStatus Status { get; set; } = BidStatus.Submitted;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>القيمة الإجمالية عند التقديم (سعر × كمية) — مخزّنة لأن حد الفرع يُقاس عليها.</summary>
    [Column(TypeName = "decimal(14,2)")]
    public decimal TotalValueAtBid { get; set; }

    /// <summary>الشركة الرئيسية التي بتّت في المزايدة المعلّقة (عند تجاوز الفرع حده).</summary>
    public string? ApprovedById { get; set; }
    public DateTime? ApprovedAt { get; set; }

    [NotMapped] public decimal TotalValue => Auction is null ? UnitPrice : UnitPrice * Auction.Quantity;
}

/// <summary>مناقصة تطرحها شركة لشراء محصول بمواصفات محددة</summary>
public class Tender
{
    public int Id { get; set; }

    [Required, MaxLength(150), Display(Name = "عنوان المناقصة")]
    public string Title { get; set; } = string.Empty;

    [Required] public string CompanyId { get; set; } = string.Empty;
    public ApplicationUser? Company { get; set; }

    [Display(Name = "المحصول")]
    public int CropId { get; set; }
    public Crop? Crop { get; set; }

    [Display(Name = "الكمية المطلوبة")]
    [Range(0.01, 10000000)]
    [Column(TypeName = "decimal(12,2)")]
    public decimal Quantity { get; set; }

    [Display(Name = "درجة الجودة المطلوبة")]
    public QualityGrade RequiredQuality { get; set; } = QualityGrade.GradeA;

    [Display(Name = "السقف السعري للوحدة")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal MaxUnitPrice { get; set; }

    [Display(Name = "تاريخ التسليم المطلوب")]
    public DateTime DeliveryDate { get; set; } = DateTime.Now.AddDays(30);

    [Display(Name = "آخر موعد لتقديم العروض")]
    public DateTime ClosingDate { get; set; } = DateTime.Now.AddDays(10);

    [MaxLength(120), Display(Name = "مكان التسليم")]
    public string? DeliveryLocation { get; set; }

    [MaxLength(1500), Display(Name = "المواصفات الفنية")]
    public string? Specifications { get; set; }

    [Display(Name = "مسؤولية اللوجستيات")]
    public LogisticsResponsibility Logistics { get; set; } = LogisticsResponsibility.Seller;

    public TenderStatus Status { get; set; } = TenderStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<TenderOffer> Offers { get; set; } = new List<TenderOffer>();

    [NotMapped] public bool IsOpen => Status == TenderStatus.Open && ClosingDate > DateTime.Now;
}

/// <summary>عرض مقدَّم على مناقصة من مزارع أو وسيط</summary>
public class TenderOffer
{
    public int Id { get; set; }

    public int TenderId { get; set; }
    public Tender? Tender { get; set; }

    [Required] public string SupplierId { get; set; } = string.Empty;
    public ApplicationUser? Supplier { get; set; }

    /// <summary>الوسيط الذي يمثل المورد في هذا العرض (إن وُجد)</summary>
    public string? BrokerId { get; set; }
    public ApplicationUser? Broker { get; set; }

    [Display(Name = "سعر الوحدة المعروض")]
    [Range(0.01, 10000000)]
    [Column(TypeName = "decimal(12,2)")]
    public decimal UnitPrice { get; set; }

    [Display(Name = "الكمية القابلة للتوريد")]
    [Range(0.01, 10000000)]
    [Column(TypeName = "decimal(12,2)")]
    public decimal AvailableQuantity { get; set; }

    [Display(Name = "أقرب موعد تسليم")]
    public DateTime? EarliestDelivery { get; set; }

    [MaxLength(800), Display(Name = "ملاحظات ومبررات العرض")]
    public string? Notes { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Submitted;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>درجة المطابقة الآلية 0-100 (السعر، الجودة، السجل، القرب)</summary>
    [Column(TypeName = "decimal(5,2)")]
    [Display(Name = "درجة المطابقة")]
    public decimal MatchScore { get; set; }

    [NotMapped] public decimal TotalValue => UnitPrice * AvailableQuantity;
}

/// <summary>العقد الرقمي الناتج عن ترسية مزاد أو مناقصة</summary>
public class Contract
{
    public int Id { get; set; }

    [MaxLength(30), Display(Name = "رقم العقد")]
    public string ContractNumber { get; set; } = string.Empty;

    [Required] public string SellerId { get; set; } = string.Empty;
    public ApplicationUser? Seller { get; set; }

    [Required] public string BuyerId { get; set; } = string.Empty;
    public ApplicationUser? Buyer { get; set; }

    public string? BrokerId { get; set; }
    public ApplicationUser? Broker { get; set; }

    public int CropId { get; set; }
    public Crop? Crop { get; set; }

    public int? AuctionId { get; set; }
    public Auction? Auction { get; set; }

    public int? TenderId { get; set; }
    public Tender? Tender { get; set; }

    [Display(Name = "الكمية")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal Quantity { get; set; }

    [Display(Name = "سعر الوحدة")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal UnitPrice { get; set; }

    [Display(Name = "القيمة الإجمالية")]
    [Column(TypeName = "decimal(14,2)")]
    public decimal TotalValue { get; set; }

    [Display(Name = "نسبة عمولة المنصة %")]
    [Column(TypeName = "decimal(5,2)")]
    public decimal PlatformCommissionRate { get; set; } = 2.0m;

    [Display(Name = "قيمة عمولة المنصة")]
    [Column(TypeName = "decimal(14,2)")]
    public decimal PlatformCommission { get; set; }

    [Display(Name = "نسبة عمولة الوسيط %")]
    [Column(TypeName = "decimal(5,2)")]
    public decimal BrokerCommissionRate { get; set; }

    [Display(Name = "قيمة عمولة الوسيط")]
    [Column(TypeName = "decimal(14,2)")]
    public decimal BrokerCommission { get; set; }

    [Display(Name = "صافي مستحق المزارع")]
    [Column(TypeName = "decimal(14,2)")]
    public decimal NetToSeller { get; set; }

    [Display(Name = "تاريخ التسليم")]
    public DateTime DeliveryDate { get; set; }

    [MaxLength(150), Display(Name = "مكان التسليم")]
    public string? DeliveryLocation { get; set; }

    [Display(Name = "مسؤولية اللوجستيات")]
    public LogisticsResponsibility Logistics { get; set; }

    [Display(Name = "حالة العقد")]
    public ContractStatus Status { get; set; } = ContractStatus.AwaitingSignatures;

    [Display(Name = "حالة الضمان المالي")]
    public EscrowStatus Escrow { get; set; } = EscrowStatus.NotFunded;

    [Display(Name = "وقّع البائع")]
    public bool SellerSigned { get; set; }

    [Display(Name = "وقّع المشتري")]
    public bool BuyerSigned { get; set; }

    [MaxLength(2000), Display(Name = "الشروط والبنود")]
    public string? Terms { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public ICollection<ContractEvent> Events { get; set; } = new List<ContractEvent>();
    public ICollection<QualityInspection> Inspections { get; set; } = new List<QualityInspection>();
}

/// <summary>سجل تدقيق لكل تغيّر في حالة العقد</summary>
public class ContractEvent
{
    public int Id { get; set; }

    public int ContractId { get; set; }
    public Contract? Contract { get; set; }

    [MaxLength(80)] public string Action { get; set; } = string.Empty;
    [MaxLength(500)] public string? Details { get; set; }
    [MaxLength(120)] public string? ActorName { get; set; }
    public string? ActorId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تقرير تحقق ميداني من الجودة قبل التسليم</summary>
public class QualityInspection
{
    public int Id { get; set; }

    public int ContractId { get; set; }
    public Contract? Contract { get; set; }

    /// <summary>الوسيط أو المدقق الميداني الذي نفّذ الزيارة</summary>
    public string? InspectorId { get; set; }
    public ApplicationUser? Inspector { get; set; }

    [Display(Name = "نتيجة التحقق")]
    public InspectionResult Result { get; set; } = InspectionResult.Pending;

    [Display(Name = "نسبة الخصم المقترحة %")]
    [Column(TypeName = "decimal(5,2)")]
    public decimal DiscountPercent { get; set; }

    [MaxLength(1000), Display(Name = "ملاحظات التحقق")]
    public string? Notes { get; set; }

    public DateTime InspectedAt { get; set; } = DateTime.Now;
}

/// <summary>طلب نقل/تخزين ضمن سوق اللوجستيات المستقل داخل المنصة</summary>
public class LogisticsRequest
{
    public int Id { get; set; }

    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    [Required] public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser? Requester { get; set; }

    [Required, MaxLength(120), Display(Name = "نقطة التحميل")]
    public string FromLocation { get; set; } = string.Empty;

    [Required, MaxLength(120), Display(Name = "نقطة التسليم")]
    public string ToLocation { get; set; } = string.Empty;

    [Display(Name = "الوزن التقريبي (كجم)")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal WeightKg { get; set; }

    [Display(Name = "يحتاج تبريد")]
    public bool NeedsRefrigeration { get; set; }

    [Display(Name = "نافذة التسليم")]
    public DateTime PickupDate { get; set; } = DateTime.Now.AddDays(3);

    [MaxLength(600), Display(Name = "تفاصيل إضافية")]
    public string? Notes { get; set; }

    public LogisticsRequestStatus Status { get; set; } = LogisticsRequestStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<LogisticsOffer> Offers { get; set; } = new List<LogisticsOffer>();
}

/// <summary>عرض مزود خدمة نقل على طلب لوجستي</summary>
public class LogisticsOffer
{
    public int Id { get; set; }

    public int LogisticsRequestId { get; set; }
    public LogisticsRequest? LogisticsRequest { get; set; }

    [Required] public string ProviderId { get; set; } = string.Empty;
    public ApplicationUser? Provider { get; set; }

    [Display(Name = "السعر المعروض")]
    [Column(TypeName = "decimal(12,2)")]
    public decimal Price { get; set; }

    [MaxLength(400), Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Submitted;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تقييم متبادل بعد إتمام العقد</summary>
public class Rating
{
    public int Id { get; set; }

    public int ContractId { get; set; }
    public Contract? Contract { get; set; }

    [Required] public string RaterId { get; set; } = string.Empty;
    public ApplicationUser? Rater { get; set; }

    [Required] public string RatedUserId { get; set; } = string.Empty;
    public ApplicationUser? RatedUser { get; set; }

    [Range(1, 5), Display(Name = "الالتزام الزمني")]
    public int Timeliness { get; set; } = 5;

    [Range(1, 5), Display(Name = "ثبات الجودة")]
    public int QualityConsistency { get; set; } = 5;

    [Range(1, 5), Display(Name = "سرعة التواصل")]
    public int Communication { get; set; } = 5;

    [MaxLength(500), Display(Name = "تعليق")]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [NotMapped] public decimal Score => Math.Round((Timeliness + QualityConsistency + Communication) / 3m, 2);
}

/// <summary>إشعار داخلي للمستخدم (يمثل ما يُرسل لاحقاً عبر واتساب)</summary>
public class Notification
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [MaxLength(150)] public string Title { get; set; } = string.Empty;
    [MaxLength(600)] public string? Body { get; set; }
    [MaxLength(200)] public string? Url { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
