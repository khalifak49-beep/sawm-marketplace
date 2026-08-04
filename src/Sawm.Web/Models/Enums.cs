namespace Sawm.Web.Models;

/// <summary>نوع الحساب في المنصة</summary>
public enum UserType
{
    Farmer = 1,   // مزارع
    Broker = 2,   // وسيط
    Company = 3,  // شركة / مشتري مؤسسي
    Admin = 4     // إدارة المنصة
}

/// <summary>نوع المزاد: لحظي على محصول جاهز، أو مستقبلي قبل الحصاد</summary>
public enum AuctionType
{
    Instant = 1,  // مزاد لحظي
    Future = 2    // مزاد مستقبلي (مبكر)
}

public enum AuctionStatus
{
    Draft = 0,      // مسودة
    Pending = 1,    // بانتظار اعتماد الوسيط
    Active = 2,     // نشط
    Closed = 3,     // مغلق (تم الترسية)
    Cancelled = 4,  // ملغى
    Expired = 5     // منتهٍ دون ترسية
}

public enum BidStatus
{
    Submitted = 1,       // مقدَّمة
    Winning = 2,         // الأعلى حالياً
    Accepted = 3,        // مقبولة
    Rejected = 4,        // مرفوضة
    Withdrawn = 5,       // مسحوبة
    PendingApproval = 6  // تجاوزت حد الفرع — بانتظار موافقة الشركة الرئيسية
}

public enum TenderStatus
{
    Open = 1,       // مفتوحة لاستقبال العروض
    UnderReview = 2,// قيد التقييم
    Awarded = 3,    // تمت الترسية
    Cancelled = 4,  // ملغاة
    Expired = 5     // منتهية
}

public enum OfferStatus
{
    Submitted = 1,
    Shortlisted = 2, // ضمن القائمة المختصرة
    Awarded = 3,     // فائزة
    Rejected = 4
}

public enum ContractStatus
{
    AwaitingSignatures = 1, // بانتظار التوقيع
    Active = 2,             // نشط
    ReadyForDelivery = 3,   // جاهز للتسليم
    Delivered = 4,          // تم التسليم
    Completed = 5,          // مكتمل ومدفوع
    Disputed = 6,           // نزاع
    Cancelled = 7           // ملغى
}

public enum EscrowStatus
{
    NotFunded = 0,  // لم يُموّل
    Held = 1,       // محتجز
    Released = 2,   // محرَّر للمزارع
    Refunded = 3    // مُعاد للمشتري
}

/// <summary>الطرف المسؤول عن النقل والتخزين والتأمين — المنصة ليست طرفاً لوجستياً</summary>
public enum LogisticsResponsibility
{
    Seller = 1,   // على البائع (المزارع)
    Buyer = 2,    // على المشتري
    Broker = 3,   // ينظمها الوسيط
    ThirdParty = 4// مزود خدمة عبر سوق اللوجستيات
}

public enum QualityGrade
{
    Premium = 1,  // ممتاز
    GradeA = 2,   // درجة أولى
    GradeB = 3,   // درجة ثانية
    GradeC = 4    // درجة ثالثة
}

public enum LogisticsRequestStatus
{
    Open = 1,
    Awarded = 2,
    Completed = 3,
    Cancelled = 4
}

public enum InspectionResult
{
    Pending = 0,
    Passed = 1,          // مطابق
    PassedWithDiscount = 2, // مطابق مع خصم متدرج
    Failed = 3           // غير مطابق
}
