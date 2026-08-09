using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Models;

namespace Sawm.Web.Data;

public class SawmDbContext : IdentityDbContext<ApplicationUser>
{
    public SawmDbContext(DbContextOptions<SawmDbContext> options) : base(options) { }

    public DbSet<FarmerProfile> FarmerProfiles => Set<FarmerProfile>();
    public DbSet<BrokerProfile> BrokerProfiles => Set<BrokerProfile>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Crop> Crops => Set<Crop>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderOffer> TenderOffers => Set<TenderOffer>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractEvent> ContractEvents => Set<ContractEvent>();
    public DbSet<QualityInspection> QualityInspections => Set<QualityInspection>();
    public DbSet<LogisticsRequest> LogisticsRequests => Set<LogisticsRequest>();
    public DbSet<LogisticsOffer> LogisticsOffers => Set<LogisticsOffer>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationEmail> NotificationEmails => Set<NotificationEmail>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ملفات تعريفية: واحد لواحد مع المستخدم
        b.Entity<FarmerProfile>()
            .HasOne(p => p.User).WithOne(u => u.FarmerProfile)
            .HasForeignKey<FarmerProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<BrokerProfile>()
            .HasOne(p => p.User).WithOne(u => u.BrokerProfile)
            .HasForeignKey<BrokerProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<CompanyProfile>(e =>
        {
            e.HasOne(p => p.User).WithOne(u => u.CompanyProfile)
                .HasForeignKey<CompanyProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            // الفرع يشير إلى مستخدم الشركة الرئيسية — Restrict لتفادي مسارات حذف متعددة
            e.HasOne(p => p.ParentCompany).WithMany()
                .HasForeignKey(p => p.ParentCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.ParentCompanyId);
        });

        // المزادات — Restrict لتفادي مسارات الحذف المتعددة على AspNetUsers
        b.Entity<Auction>(e =>
        {
            e.HasOne(a => a.Farmer).WithMany().HasForeignKey(a => a.FarmerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Broker).WithMany().HasForeignKey(a => a.BrokerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Crop).WithMany(c => c.Auctions).HasForeignKey(a => a.CropId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.Status, a.EndDate });
        });

        b.Entity<Bid>(e =>
        {
            e.HasOne(x => x.Auction).WithMany(a => a.Bids).HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Bidder).WithMany().HasForeignKey(x => x.BidderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AuctionId, x.UnitPrice });
        });

        b.Entity<Tender>(e =>
        {
            e.HasOne(t => t.Company).WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Crop).WithMany(c => c.Tenders).HasForeignKey(t => t.CropId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => new { t.Status, t.ClosingDate });
        });

        b.Entity<TenderOffer>(e =>
        {
            e.HasOne(o => o.Tender).WithMany(t => t.Offers).HasForeignKey(o => o.TenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Broker).WithMany().HasForeignKey(o => o.BrokerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Contract>(e =>
        {
            e.HasOne(c => c.Seller).WithMany().HasForeignKey(c => c.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Buyer).WithMany().HasForeignKey(c => c.BuyerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Broker).WithMany().HasForeignKey(c => c.BrokerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Crop).WithMany().HasForeignKey(c => c.CropId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Auction).WithMany().HasForeignKey(c => c.AuctionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.Tender).WithMany().HasForeignKey(c => c.TenderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(c => c.ContractNumber).IsUnique();
        });

        b.Entity<ContractEvent>()
            .HasOne(x => x.Contract).WithMany(c => c.Events)
            .HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<QualityInspection>(e =>
        {
            e.HasOne(x => x.Contract).WithMany(c => c.Inspections).HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Inspector).WithMany().HasForeignKey(x => x.InspectorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LogisticsRequest>(e =>
        {
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LogisticsOffer>(e =>
        {
            e.HasOne(x => x.LogisticsRequest).WithMany(r => r.Offers).HasForeignKey(x => x.LogisticsRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Rating>(e =>
        {
            e.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Rater).WithMany().HasForeignKey(x => x.RaterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RatedUser).WithMany().HasForeignKey(x => x.RatedUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ContractId, x.RaterId, x.RatedUserId }).IsUnique();
        });

        b.Entity<Notification>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.IsRead });
        });
    }
}
