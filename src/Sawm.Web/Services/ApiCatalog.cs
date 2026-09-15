using System.Text.Json;
using Sawm.Web.Models;

namespace Sawm.Web.Services;

public record ApiFieldDef(string Key, string Label);
public record ApiResourceDef(string Key, string Label, string Endpoint, ApiFieldDef[] Fields);

/// <summary>
/// كتالوج الموارد والحقول المتاحة عبر الـAPI العمومي، ودوال إسقاط الكيانات إلى قواميس.
/// ملاحظة أمنية: لا يحتوي الكتالوج على أي حقول هويّة (المزارع/الشركة/البائع/المشتري/الوسيط)
/// احتراماً لإخفاء هويّة الأطراف؛ تُعرض بيانات السوق والعقود دون كشف من هم أطرافها.
/// مفاتيح الحقول هنا يجب أن تطابق مفاتيح القواميس في دوال Map* أدناه.
/// </summary>
public static class ApiCatalog
{
    public static readonly ApiResourceDef[] Resources =
    {
        new("auctions", "المزادات", "/api/v1/auctions", new ApiFieldDef[]
        {
            new("id", "المعرّف"),
            new("title", "عنوان المزاد"),
            new("crop", "المحصول"),
            new("quantity", "الكمية"),
            new("unit", "الوحدة"),
            new("quality", "درجة الجودة"),
            new("startPrice", "سعر الافتتاح"),
            new("currentPrice", "السعر الحالي"),
            new("type", "نوع المزاد"),
            new("status", "الحالة"),
            new("startDate", "تاريخ البدء"),
            new("endDate", "تاريخ الإغلاق"),
            new("expectedHarvestDate", "تاريخ الحصاد المتوقع"),
            new("pickupLocation", "موقع الاستلام"),
            new("description", "الوصف"),
            new("logistics", "مسؤولية اللوجستيات"),
            new("estimatedValue", "القيمة التقديرية"),
            new("createdAt", "تاريخ الإنشاء"),
        }),
        new("tenders", "المناقصات", "/api/v1/tenders", new ApiFieldDef[]
        {
            new("id", "المعرّف"),
            new("title", "عنوان المناقصة"),
            new("crop", "المحصول"),
            new("quantity", "الكمية المطلوبة"),
            new("requiredQuality", "الجودة المطلوبة"),
            new("maxUnitPrice", "السقف السعري للوحدة"),
            new("deliveryDate", "تاريخ التسليم"),
            new("closingDate", "آخر موعد للعروض"),
            new("deliveryLocation", "مكان التسليم"),
            new("specifications", "المواصفات الفنية"),
            new("logistics", "مسؤولية اللوجستيات"),
            new("status", "الحالة"),
            new("offersCount", "عدد العروض"),
            new("createdAt", "تاريخ الإنشاء"),
        }),
        new("contracts", "العقود", "/api/v1/contracts", new ApiFieldDef[]
        {
            new("id", "المعرّف"),
            new("contractNumber", "رقم العقد"),
            new("crop", "المحصول"),
            new("quantity", "الكمية"),
            new("unitPrice", "سعر الوحدة"),
            new("totalValue", "القيمة الإجمالية"),
            new("platformCommissionRate", "نسبة عمولة المنصة %"),
            new("platformCommission", "قيمة عمولة المنصة"),
            new("brokerCommissionRate", "نسبة عمولة الوسيط %"),
            new("brokerCommission", "قيمة عمولة الوسيط"),
            new("netToSeller", "صافي مستحق المزارع"),
            new("deliveryDate", "تاريخ التسليم"),
            new("deliveryLocation", "مكان التسليم"),
            new("logistics", "مسؤولية اللوجستيات"),
            new("status", "حالة العقد"),
            new("escrow", "حالة الضمان المالي"),
            new("paymentMethod", "وسيلة الدفع"),
            new("paidAt", "تاريخ الدفع"),
            new("sellerSigned", "وقّع البائع"),
            new("buyerSigned", "وقّع المشتري"),
            new("createdAt", "تاريخ الإنشاء"),
            new("completedAt", "تاريخ الاكتمال"),
        }),
        new("crops", "المحاصيل", "/api/v1/crops", new ApiFieldDef[]
        {
            new("id", "المعرّف"),
            new("name", "الاسم"),
            new("category", "التصنيف"),
            new("unit", "وحدة القياس"),
            new("referencePrice", "السعر المرجعي"),
            new("isActive", "نشط"),
        }),
        new("shipments", "طلبات الشحن", "/api/v1/shipments", new ApiFieldDef[]
        {
            new("id", "المعرّف"),
            new("contractNumber", "رقم العقد"),
            new("crop", "المحصول"),
            new("quantity", "الكمية"),
            new("unit", "الوحدة"),
            new("pickupLocation", "موقع الاستلام"),
            new("deliveryLocation", "مكان التسليم"),
            new("deliveryDate", "تاريخ التسليم"),
            new("logistics", "مسؤولية اللوجستيات"),
            new("commissionRate", "نسبة عمولة ساوم %"),
            new("commissionAmount", "قيمة عمولة ساوم"),
            new("status", "حالة العقد"),
            new("stage", "مرحلة الشحن"),
            new("received", "تم استلامها"),
            new("receivedAt", "تاريخ الاستلام"),
            new("releasedAt", "تاريخ الإرسال للشحن"),
            new("createdAt", "تاريخ الإنشاء"),
        }),
    };

    /// <summary>رمز مرحلة الشحن في الـAPI (لغة النظام الخارجي).</summary>
    public static string StageCode(ShipmentStage s) => s switch
    {
        ShipmentStage.Received => "received",
        ShipmentStage.InTransit => "in_transit",
        ShipmentStage.Delivered => "delivered",
        _ => "pending"
    };

    public static ShipmentStage? ParseStage(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "received" => ShipmentStage.Received,
        "in_transit" or "intransit" or "in-transit" => ShipmentStage.InTransit,
        "delivered" => ShipmentStage.Delivered,
        _ => null
    };

    public static ApiResourceDef? Find(string key) =>
        Array.Find(Resources, r => r.Key == key);

    // ── إسقاط الكيانات إلى قواميس (كل الحقول الآمنة؛ يُرشَّح لاحقاً حسب المفاتيح المسموحة) ──

    public static Dictionary<string, object?> MapAuction(Auction a) => new()
    {
        ["id"] = a.Id,
        ["title"] = a.Title,
        ["crop"] = a.Crop?.Name,
        ["quantity"] = a.Quantity,
        ["unit"] = a.DisplayUnit,
        ["quality"] = a.Quality.ToString(),
        ["startPrice"] = a.StartPrice,
        ["currentPrice"] = a.CurrentPrice,
        ["type"] = a.Type.ToString(),
        ["status"] = a.Status.ToString(),
        ["startDate"] = a.StartDate,
        ["endDate"] = a.EndDate,
        ["expectedHarvestDate"] = a.ExpectedHarvestDate,
        ["pickupLocation"] = a.PickupLocation,
        ["description"] = a.Description,
        ["logistics"] = a.Logistics.ToString(),
        ["estimatedValue"] = a.EstimatedValue,
        ["createdAt"] = a.CreatedAt,
    };

    public static Dictionary<string, object?> MapTender(Tender t) => new()
    {
        ["id"] = t.Id,
        ["title"] = t.Title,
        ["crop"] = t.Crop?.Name,
        ["quantity"] = t.Quantity,
        ["requiredQuality"] = t.RequiredQuality.ToString(),
        ["maxUnitPrice"] = t.MaxUnitPrice,
        ["deliveryDate"] = t.DeliveryDate,
        ["closingDate"] = t.ClosingDate,
        ["deliveryLocation"] = t.DeliveryLocation,
        ["specifications"] = t.Specifications,
        ["logistics"] = t.Logistics.ToString(),
        ["status"] = t.Status.ToString(),
        ["offersCount"] = t.Offers?.Count ?? 0,
        ["createdAt"] = t.CreatedAt,
    };

    public static Dictionary<string, object?> MapContract(Contract c) => new()
    {
        ["id"] = c.Id,
        ["contractNumber"] = c.ContractNumber,
        ["crop"] = c.Crop?.Name,
        ["quantity"] = c.Quantity,
        ["unitPrice"] = c.UnitPrice,
        ["totalValue"] = c.TotalValue,
        ["platformCommissionRate"] = c.PlatformCommissionRate,
        ["platformCommission"] = c.PlatformCommission,
        ["brokerCommissionRate"] = c.BrokerCommissionRate,
        ["brokerCommission"] = c.BrokerCommission,
        ["netToSeller"] = c.NetToSeller,
        ["deliveryDate"] = c.DeliveryDate,
        ["deliveryLocation"] = c.DeliveryLocation,
        ["logistics"] = c.Logistics.ToString(),
        ["status"] = c.Status.ToString(),
        ["escrow"] = c.Escrow.ToString(),
        ["paymentMethod"] = c.PaymentMethod,
        ["paidAt"] = c.PaidAt,
        ["sellerSigned"] = c.SellerSigned,
        ["buyerSigned"] = c.BuyerSigned,
        ["createdAt"] = c.CreatedAt,
        ["completedAt"] = c.CompletedAt,
    };

    public static Dictionary<string, object?> MapShipment(Contract c) => new()
    {
        ["id"] = c.Id,
        ["contractNumber"] = c.ContractNumber,
        ["crop"] = c.Crop?.Name,
        ["quantity"] = c.Quantity,
        ["unit"] = c.Crop?.Unit,
        ["pickupLocation"] = c.Auction?.PickupLocation,
        ["deliveryLocation"] = c.DeliveryLocation,
        ["deliveryDate"] = c.DeliveryDate,
        ["logistics"] = c.Logistics.ToString(),
        ["commissionRate"] = c.ShippingCommissionRate,
        ["commissionAmount"] = Math.Round(c.TotalValue * c.ShippingCommissionRate / 100m, 2),
        ["status"] = c.Status.ToString(),
        ["stage"] = StageCode(c.ShipmentStage),
        ["received"] = c.ShippingReceived,
        ["receivedAt"] = c.ShippingReceivedAt,
        ["releasedAt"] = c.ShippingReleasedAt,
        ["createdAt"] = c.CreatedAt,
    };

    public static Dictionary<string, object?> MapCrop(Crop c) => new()
    {
        ["id"] = c.Id,
        ["name"] = c.Name,
        ["category"] = c.Category,
        ["unit"] = c.Unit,
        ["referencePrice"] = c.ReferencePrice,
        ["isActive"] = c.IsActive,
    };

    /// <summary>يُبقي فقط الحقول المسموح بها (بترتيب الكتالوج) من القاموس الكامل.</summary>
    public static Dictionary<string, object?> Filter(Dictionary<string, object?> full, IEnumerable<string> allowed)
    {
        var result = new Dictionary<string, object?>();
        foreach (var key in allowed)
            if (full.TryGetValue(key, out var val))
                result[key] = val;
        return result;
    }

    /// <summary>يقرأ خريطة الموارد→الحقول المخزّنة في مفتاح API.</summary>
    public static Dictionary<string, string[]> ParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
            return parsed ?? new();
        }
        catch { return new(); }
    }

    /// <summary>يبني JSON صالحاً من اختيارات "resource:field" مع التحقق من الكتالوج.</summary>
    public static string BuildFieldsJson(IEnumerable<string> resourceFieldPairs)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var pair in resourceFieldPairs)
        {
            var i = pair.IndexOf(':');
            if (i <= 0) continue;
            var res = pair[..i];
            var field = pair[(i + 1)..];
            var def = Find(res);
            if (def is null) continue;
            if (!Array.Exists(def.Fields, f => f.Key == field)) continue;
            if (!map.TryGetValue(res, out var list)) { list = new(); map[res] = list; }
            if (!list.Contains(field)) list.Add(field);
        }
        // رتّب الحقول حسب ترتيب الكتالوج للثبات
        var ordered = new Dictionary<string, string[]>();
        foreach (var def in Resources)
            if (map.TryGetValue(def.Key, out var chosen))
                ordered[def.Key] = def.Fields.Where(f => chosen.Contains(f.Key)).Select(f => f.Key).ToArray();
        return JsonSerializer.Serialize(ordered);
    }
}
