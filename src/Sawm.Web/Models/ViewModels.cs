using System.ComponentModel.DataAnnotations;

namespace Sawm.Web.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "صيغة البريد غير صحيحة")]
    [Display(Name = "البريد الإلكتروني")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "تذكّرني")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [MaxLength(120)]
    [Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "صيغة البريد غير صحيحة")]
    [Display(Name = "البريد الإلكتروني")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم الهاتف غير صالح")]
    [Display(Name = "رقم الهاتف (واتساب)")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "اختر نوع الحساب")]
    [Display(Name = "نوع الحساب")]
    public UserType UserType { get; set; } = UserType.Farmer;

    [Required(ErrorMessage = "المنطقة مطلوبة")]
    [MaxLength(80)]
    [Display(Name = "المحافظة / المنطقة")]
    public string Region { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور يجب ألا تقل عن 6 أحرف")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "كلمتا المرور غير متطابقتين")]
    [Display(Name = "تأكيد كلمة المرور")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // حقول المزارع
    [Display(Name = "المساحة الزراعية (فدان)")]
    public decimal? FarmArea { get; set; }

    [MaxLength(60), Display(Name = "نوع التربة")]
    public string? SoilType { get; set; }

    [MaxLength(60), Display(Name = "مصدر الري")]
    public string? IrrigationSource { get; set; }

    // حقول الوسيط
    [MaxLength(60), Display(Name = "رقم الترخيص")]
    public string? LicenseNumber { get; set; }

    [Display(Name = "نسبة العمولة %")]
    [Range(0, 30, ErrorMessage = "النسبة يجب أن تكون بين 0 و 30")]
    public decimal? CommissionRate { get; set; }

    // حقول الشركة
    [MaxLength(150), Display(Name = "اسم الشركة")]
    public string? CompanyName { get; set; }

    [MaxLength(60), Display(Name = "السجل التجاري")]
    public string? CommercialRegistry { get; set; }

    [MaxLength(80), Display(Name = "طبيعة النشاط")]
    public string? ActivityType { get; set; }
}

/// <summary>ملخص لوحة التحكم — يتكيف مع دور المستخدم</summary>
public class DashboardViewModel
{
    public string FullName { get; set; } = string.Empty;
    public UserType UserType { get; set; }
    public string RoleLabel { get; set; } = string.Empty;

    public int ActiveAuctions { get; set; }
    public int OpenTenders { get; set; }
    public int MyBids { get; set; }
    public int MyOffers { get; set; }
    public int ActiveContracts { get; set; }
    public int CompletedContracts { get; set; }
    public decimal TotalTradedValue { get; set; }
    public decimal PendingEscrow { get; set; }
    public decimal RatingAverage { get; set; }
    public int UnreadNotifications { get; set; }

    public List<Auction> RecentAuctions { get; set; } = new();
    public List<Tender> RecentTenders { get; set; } = new();
    public List<Contract> RecentContracts { get; set; } = new();
    public List<Notification> LatestNotifications { get; set; } = new();

    /// <summary>مؤشرات السوق: متوسط سعر آخر عقد لكل محصول</summary>
    public List<MarketPricePoint> MarketPrices { get; set; } = new();
}

public class MarketPricePoint
{
    public string CropName { get; set; } = string.Empty;
    public string Unit { get; set; } = "كجم";
    public decimal ReferencePrice { get; set; }
    public decimal LatestTradedPrice { get; set; }
    public int DealsCount { get; set; }

    public decimal ChangePercent =>
        ReferencePrice == 0 ? 0 : Math.Round((LatestTradedPrice - ReferencePrice) / ReferencePrice * 100m, 1);
}

public class PlaceBidViewModel
{
    public int AuctionId { get; set; }
    public string AuctionTitle { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal MinIncrement { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "كجم";

    [Required(ErrorMessage = "أدخل سعر الوحدة")]
    [Range(0.01, 10000000, ErrorMessage = "سعر غير صالح")]
    [Display(Name = "سعر الوحدة المعروض")]
    public decimal UnitPrice { get; set; }

    [MaxLength(400), Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class SubmitOfferViewModel
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal MaxUnitPrice { get; set; }
    public string Unit { get; set; } = "كجم";

    [Required(ErrorMessage = "أدخل سعر الوحدة")]
    [Range(0.01, 10000000, ErrorMessage = "سعر غير صالح")]
    [Display(Name = "سعر الوحدة")]
    public decimal UnitPrice { get; set; }

    [Required(ErrorMessage = "أدخل الكمية")]
    [Range(0.01, 10000000, ErrorMessage = "كمية غير صالحة")]
    [Display(Name = "الكمية القابلة للتوريد")]
    public decimal AvailableQuantity { get; set; }

    [Display(Name = "أقرب موعد تسليم")]
    [DataType(DataType.Date)]
    public DateTime? EarliestDelivery { get; set; }

    /// <summary>المزارع الذي يمثله الوسيط — يُستخدم عندما يقدم وسيط العرض</summary>
    [Display(Name = "المزارع الذي تمثله")]
    public string? RepresentedFarmerId { get; set; }

    [MaxLength(800), Display(Name = "ملاحظات ومبررات العرض")]
    public string? Notes { get; set; }
}

/// <summary>عرض صفحة الوسيط: المزارعون تحت تمثيله والفرص المتاحة</summary>
public class BrokerWorkspaceViewModel
{
    public List<Auction> PendingApproval { get; set; } = new();
    public List<Auction> ManagedAuctions { get; set; } = new();
    public List<Tender> OpenTenders { get; set; } = new();
    public List<Contract> BrokeredContracts { get; set; } = new();
    public decimal EarnedCommission { get; set; }
    public decimal PipelineCommission { get; set; }
    public decimal CommissionRate { get; set; }
}
