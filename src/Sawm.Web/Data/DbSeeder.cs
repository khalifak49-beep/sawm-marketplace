using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Data;

/// <summary>يهيئ قاعدة البيانات: الأدوار، حسابات تجريبية، محاصيل، ومزادات/مناقصات للعرض</summary>
public static class DbSeeder
{
    public const string DemoPassword = "Sawm@2026";

    public static async Task SeedAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SawmDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // SQLite (سحابة) يُنشئ المخطط من النموذج مباشرة بلا ترحيلات؛ SQL Server يطبّق الترحيلات
        if (db.Database.IsSqlite())
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        foreach (var r in new[] { Roles.Farmer, Roles.Broker, Roles.Company, Roles.Admin })
            if (!await roles.RoleExistsAsync(r))
                await roles.CreateAsync(new IdentityRole(r));

        // مستلم إشعارات افتراضي — بريد المتابعة يصله نسخة من كل الإشعارات
        if (!await db.NotificationEmails.AnyAsync())
        {
            db.NotificationEmails.Add(new NotificationEmail { Email = "service@althiqaom.com", Label = "بريد المنصة" });
            await db.SaveChangesAsync();
        }

        if (!await db.Crops.AnyAsync())
        {
            db.Crops.AddRange(
                new Crop { Name = "تمور خلاص", Category = "تمور", Unit = "كجم", ReferencePrice = 3.20m },
                new Crop { Name = "تمور فرض", Category = "تمور", Unit = "كجم", ReferencePrice = 2.10m },
                new Crop { Name = "طماطم", Category = "خضروات", Unit = "كجم", ReferencePrice = 0.55m },
                new Crop { Name = "خيار", Category = "خضروات", Unit = "كجم", ReferencePrice = 0.45m },
                new Crop { Name = "بطاطس", Category = "خضروات", Unit = "كجم", ReferencePrice = 0.38m },
                new Crop { Name = "بصل", Category = "خضروات", Unit = "كجم", ReferencePrice = 0.32m },
                new Crop { Name = "ليمون عماني", Category = "فواكه", Unit = "كجم", ReferencePrice = 1.40m },
                new Crop { Name = "مانجو", Category = "فواكه", Unit = "كجم", ReferencePrice = 1.80m },
                new Crop { Name = "موز", Category = "فواكه", Unit = "كجم", ReferencePrice = 0.70m },
                new Crop { Name = "برسيم (علف)", Category = "أعلاف", Unit = "طن", ReferencePrice = 95.00m }
            );
            await db.SaveChangesAsync();
        }

        // حساب الإدارة
        var admin = await EnsureUserAsync(users, "admin@sawm.om", "مدير المنصة", UserType.Admin, "مسقط", "96890000001");

        // مزارعون
        var f1 = await EnsureUserAsync(users, "farmer1@sawm.om", "سالم بن ناصر الهنائي", UserType.Farmer, "الباطنة شمال", "96891000001");
        var f2 = await EnsureUserAsync(users, "farmer2@sawm.om", "خالد بن سعيد المعمري", UserType.Farmer, "الداخلية", "96891000002");
        var f3 = await EnsureUserAsync(users, "farmer3@sawm.om", "عائشة بنت حمد البلوشية", UserType.Farmer, "ظفار", "96891000003");

        // وسطاء
        var b1 = await EnsureUserAsync(users, "broker1@sawm.om", "مؤسسة الوصل للتسويق الزراعي", UserType.Broker, "مسقط", "96892000001");
        var b2 = await EnsureUserAsync(users, "broker2@sawm.om", "شركة الميزان للوساطة", UserType.Broker, "الباطنة جنوب", "96892000002");

        // شركات مشترية
        var c1 = await EnsureUserAsync(users, "company1@sawm.om", "سلسلة أسواق الخليج", UserType.Company, "مسقط", "96893000001");
        var c2 = await EnsureUserAsync(users, "company2@sawm.om", "فنادق الشاطئ للضيافة", UserType.Company, "ظفار", "96893000002");

        await db.SaveChangesAsync();

        // الملفات التعريفية
        if (!await db.FarmerProfiles.AnyAsync())
        {
            db.FarmerProfiles.AddRange(
                new FarmerProfile { UserId = f1.Id, FarmArea = 45, SoilType = "طميية", IrrigationSource = "بئر ارتوازي", FarmLocation = "صحار - وادي حبيب", ExperienceYears = 18 },
                new FarmerProfile { UserId = f2.Id, FarmArea = 12, SoilType = "رملية", IrrigationSource = "أفلاج", FarmLocation = "نزوى - بركة الموز", ExperienceYears = 7 },
                new FarmerProfile { UserId = f3.Id, FarmArea = 80, SoilType = "طينية", IrrigationSource = "ري بالتنقيط", FarmLocation = "صلالة - عوقد", ExperienceYears = 25 }
            );
            db.BrokerProfiles.AddRange(
                new BrokerProfile { UserId = b1.Id, LicenseNumber = "BRK-1188", CommissionRate = 2.5m, CoverageArea = "مسقط والباطنة", ClosedDeals = 64 },
                new BrokerProfile { UserId = b2.Id, LicenseNumber = "BRK-2043", CommissionRate = 3.0m, CoverageArea = "الباطنة والداخلية", ClosedDeals = 31 }
            );
            db.CompanyProfiles.AddRange(
                new CompanyProfile { UserId = c1.Id, CompanyName = "سلسلة أسواق الخليج", CommercialRegistry = "CR-778120", ActivityType = "تجزئة غذائية", MonthlyDemand = 420 },
                new CompanyProfile { UserId = c2.Id, CompanyName = "فنادق الشاطئ للضيافة", CommercialRegistry = "CR-661903", ActivityType = "ضيافة ومطاعم", MonthlyDemand = 95 }
            );
            await db.SaveChangesAsync();
        }

        // فروع تجريبية لـ"سلسلة أسواق الخليج" (شركة رئيسية c1)
        if (!await db.CompanyProfiles.AnyAsync(p => p.ParentCompanyId != null))
        {
            var br1 = await EnsureUserAsync(users, "branch.muscat@sawm.om", "سلسلة أسواق الخليج — فرع مسقط", UserType.Company, "مسقط", "96893100001");
            var br2 = await EnsureUserAsync(users, "branch.salalah@sawm.om", "سلسلة أسواق الخليج — فرع صلالة", UserType.Company, "ظفار", "96893100002");
            db.CompanyProfiles.AddRange(
                new CompanyProfile
                {
                    UserId = br1.Id, CompanyName = "فرع مسقط", ActivityType = "تجزئة غذائية",
                    ParentCompanyId = c1.Id, BidLimit = 3000m,
                    CanBid = true, CanCreateTenders = false, CanManageContracts = false
                },
                new CompanyProfile
                {
                    UserId = br2.Id, CompanyName = "فرع صلالة", ActivityType = "تجزئة غذائية",
                    ParentCompanyId = c1.Id, BidLimit = 6000m,
                    CanBid = true, CanCreateTenders = true, CanManageContracts = true
                }
            );
            await db.SaveChangesAsync();
        }

        // مزادات تجريبية
        if (!await db.Auctions.AnyAsync())
        {
            var tomato = await db.Crops.FirstAsync(c => c.Name == "طماطم");
            var dates = await db.Crops.FirstAsync(c => c.Name == "تمور خلاص");
            var lime = await db.Crops.FirstAsync(c => c.Name == "ليمون عماني");

            var a1 = new Auction
            {
                Title = "طماطم درجة أولى — دفعة 8 أطنان",
                FarmerId = f1.Id, BrokerId = b1.Id, CropId = tomato.Id,
                Quantity = 8000, Quality = QualityGrade.GradeA, StartPrice = 0.48m, MinIncrement = 0.01m,
                Type = AuctionType.Instant, Status = AuctionStatus.Active,
                StartDate = DateTime.Now.AddDays(-2), EndDate = DateTime.Now.AddDays(4),
                PickupLocation = "صحار", Logistics = LogisticsResponsibility.Buyer,
                Description = "طماطم حقلية، فرز يدوي، عبوات 10 كجم، جاهزة للتحميل خلال 48 ساعة."
            };
            var a2 = new Auction
            {
                Title = "تمور خلاص — موسم قادم (بيع مبكر)",
                FarmerId = f3.Id, BrokerId = b1.Id, CropId = dates.Id,
                Quantity = 15000, Quality = QualityGrade.Premium, StartPrice = 2.95m, MinIncrement = 0.05m,
                Type = AuctionType.Future, Status = AuctionStatus.Active,
                StartDate = DateTime.Now.AddDays(-5), EndDate = DateTime.Now.AddDays(10),
                ExpectedHarvestDate = DateTime.Now.AddMonths(4),
                PickupLocation = "صلالة", Logistics = LogisticsResponsibility.Seller,
                Description = "تعاقد مبكر قبل الحصاد بأربعة أشهر، مع تحقق ميداني للجودة قبل التسليم."
            };
            var a3 = new Auction
            {
                Title = "ليمون عماني مجفف — 2 طن",
                FarmerId = f2.Id, CropId = lime.Id,
                Quantity = 2000, Quality = QualityGrade.GradeB, StartPrice = 1.25m, MinIncrement = 0.02m,
                Type = AuctionType.Instant, Status = AuctionStatus.Pending,
                StartDate = DateTime.Now, EndDate = DateTime.Now.AddDays(7),
                PickupLocation = "نزوى", Logistics = LogisticsResponsibility.Broker,
                Description = "بانتظار إسناد وسيط لاعتماد المزاد وفتحه للمزايدة."
            };
            db.Auctions.AddRange(a1, a2, a3);
            await db.SaveChangesAsync();

            db.Bids.AddRange(
                new Bid { AuctionId = a1.Id, BidderId = c1.Id, UnitPrice = 0.52m, CreatedAt = DateTime.Now.AddDays(-1), Notes = "نطلب تسليم على دفعتين." },
                new Bid { AuctionId = a1.Id, BidderId = b2.Id, UnitPrice = 0.55m, CreatedAt = DateTime.Now.AddHours(-8), Status = BidStatus.Winning },
                new Bid { AuctionId = a2.Id, BidderId = c2.Id, UnitPrice = 3.10m, CreatedAt = DateTime.Now.AddDays(-2), Status = BidStatus.Winning }
            );
            await db.SaveChangesAsync();
        }

        // مناقصات تجريبية
        if (!await db.Tenders.AnyAsync())
        {
            var potato = await db.Crops.FirstAsync(c => c.Name == "بطاطس");
            var cucumber = await db.Crops.FirstAsync(c => c.Name == "خيار");

            var t1 = new Tender
            {
                Title = "توريد بطاطس شهري لفروع المنطقة الوسطى",
                CompanyId = c1.Id, CropId = potato.Id, Quantity = 25000,
                RequiredQuality = QualityGrade.GradeA, MaxUnitPrice = 0.42m,
                DeliveryDate = DateTime.Now.AddDays(35), ClosingDate = DateTime.Now.AddDays(9),
                DeliveryLocation = "مستودع مسقط المركزي", Logistics = LogisticsResponsibility.Seller,
                Specifications = "حجم 60-90 ملم، خالية من التبقع، عبوات 25 كجم، شهادة سلامة غذائية سارية."
            };
            var t2 = new Tender
            {
                Title = "خيار طازج أسبوعي لمطابخ الفنادق",
                CompanyId = c2.Id, CropId = cucumber.Id, Quantity = 4000,
                RequiredQuality = QualityGrade.Premium, MaxUnitPrice = 0.60m,
                DeliveryDate = DateTime.Now.AddDays(14), ClosingDate = DateTime.Now.AddDays(5),
                DeliveryLocation = "صلالة - المطبخ المركزي", Logistics = LogisticsResponsibility.Buyer,
                Specifications = "طول موحد 14-18 سم، سلسلة تبريد متصلة، تسليم صباحي قبل 8:00."
            };
            db.Tenders.AddRange(t1, t2);
            await db.SaveChangesAsync();

            var matcher = new MatchingService();
            var o1 = new TenderOffer { TenderId = t1.Id, SupplierId = f1.Id, BrokerId = b1.Id, UnitPrice = 0.39m, AvailableQuantity = 25000, EarliestDelivery = DateTime.Now.AddDays(30), Notes = "توريد على أربع دفعات أسبوعية." };
            var o2 = new TenderOffer { TenderId = t1.Id, SupplierId = f2.Id, UnitPrice = 0.36m, AvailableQuantity = 14000, EarliestDelivery = DateTime.Now.AddDays(33), Notes = "تغطية جزئية بسعر أفضل." };
            var o3 = new TenderOffer { TenderId = t2.Id, SupplierId = f3.Id, BrokerId = b2.Id, UnitPrice = 0.55m, AvailableQuantity = 4000, EarliestDelivery = DateTime.Now.AddDays(12) };

            foreach (var (offer, tender, supplierId) in new[] { (o1, t1, f1.Id), (o2, t1, f2.Id), (o3, t2, f3.Id) })
            {
                var supplier = await users.FindByIdAsync(supplierId);
                offer.MatchScore = matcher.ScoreOffer(tender, offer, supplier);
            }
            db.TenderOffers.AddRange(o1, o2, o3);
            await db.SaveChangesAsync();
        }

        // عقود تجريبية = شحنات جاهزة/نشطة تحتاج نقلاً (تظهر لمنصة الشحن عبر الـAPI)
        if (!await db.Contracts.AnyAsync())
        {
            var tomato = await db.Crops.FirstAsync(c => c.Name == "طماطم");
            var dates = await db.Crops.FirstAsync(c => c.Name == "تمور خلاص");
            var potatoC = await db.Crops.FirstAsync(c => c.Name == "بطاطس");

            Contract MakeContract(string number, ApplicationUser seller, ApplicationUser buyer, ApplicationUser? broker,
                Crop crop, decimal qtyTons, decimal unitPrice, int deliveryInDays, string deliveryLocation, ContractStatus status)
            {
                var total = qtyTons * unitPrice;
                return new Contract
                {
                    ContractNumber = number, SellerId = seller.Id, BuyerId = buyer.Id, BrokerId = broker?.Id,
                    CropId = crop.Id, Quantity = qtyTons, UnitPrice = unitPrice, TotalValue = total,
                    PlatformCommissionRate = 2m, PlatformCommission = total * 0.02m,
                    NetToSeller = total * 0.98m,
                    DeliveryDate = DateTime.Now.AddDays(deliveryInDays), DeliveryLocation = deliveryLocation,
                    Logistics = LogisticsResponsibility.ThirdParty, Status = status, Escrow = EscrowStatus.Held,
                    SellerSigned = true, BuyerSigned = true
                };
            }

            db.Contracts.AddRange(
                MakeContract("SAWM-2026-1001", f1, c1, b1, tomato, 8m, 0.55m, 3, "مسقط — المستودع المركزي", ContractStatus.ReadyForDelivery),
                MakeContract("SAWM-2026-1002", f3, c2, b1, dates, 15m, 3.10m, 6, "صلالة — فنادق الشاطئ", ContractStatus.Active),
                MakeContract("SAWM-2026-1003", f2, c1, null, potatoC, 12m, 0.38m, 4, "مسقط — مستودع الخليج", ContractStatus.ReadyForDelivery)
            );
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> users, string email, string fullName, UserType type, string region, string phone)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is not null) return user;

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PhoneNumber = phone,
            FullName = fullName,
            UserType = type,
            Region = region,
            IsVerified = true,
            RatingAverage = type == UserType.Admin ? 0 : 4.3m,
            RatingCount = type == UserType.Admin ? 0 : 12
        };

        var result = await users.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException($"تعذّر إنشاء المستخدم {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        await users.AddToRoleAsync(user, Roles.For(type));
        return user;
    }
}
