using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// اختيار مزوّد قاعدة البيانات:
//   DB_PROVIDER=sqlite  → قاعدة ملفّية خفيفة (للسحابة/Codespaces، تعمل دون SQL Server)
//   غير ذلك            → SQL Server (البيئة المحلية الافتراضية)
var dbProvider = builder.Configuration["DB_PROVIDER"]
    ?? Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "sqlserver";

if (string.Equals(dbProvider, "sqlite", StringComparison.OrdinalIgnoreCase))
{
    var sqlitePath = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=sawm.db";
    builder.Services.AddDbContext<SawmDbContext>(options => options.UseSqlite(sqlitePath));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("سلسلة الاتصال DefaultConnection غير معرّفة.");
    builder.Services.AddDbContext<SawmDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<SawmDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddScoped<BranchService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// تهيئة قاعدة البيانات والبيانات التجريبية عند الإقلاع
await DbSeeder.SeedAsync(app.Services);

app.Run();
