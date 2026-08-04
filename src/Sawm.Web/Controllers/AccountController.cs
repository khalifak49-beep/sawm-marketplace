using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly SawmDbContext _db;

    public AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, SawmDbContext db)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "تم قفل الحساب مؤقتاً بسبب محاولات دخول متكررة."
            : "البريد الإلكتروني أو كلمة المرور غير صحيحة.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new RegisterViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (model.UserType == UserType.Company && string.IsNullOrWhiteSpace(model.CompanyName))
            ModelState.AddModelError(nameof(model.CompanyName), "اسم الشركة مطلوب لحساب الشركات.");

        if (model.UserType == UserType.Admin)
            ModelState.AddModelError(nameof(model.UserType), "لا يمكن إنشاء حساب إدارة من هذه الصفحة.");

        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            PhoneNumber = model.PhoneNumber,
            FullName = model.FullName,
            UserType = model.UserType,
            Region = model.Region
        };

        var created = await _users.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            foreach (var e in created.Errors) ModelState.AddModelError(string.Empty, Translate(e.Code, e.Description));
            return View(model);
        }

        await _users.AddToRoleAsync(user, Roles.For(model.UserType));

        switch (model.UserType)
        {
            case UserType.Farmer:
                _db.FarmerProfiles.Add(new FarmerProfile
                {
                    UserId = user.Id,
                    FarmArea = model.FarmArea ?? 0,
                    SoilType = model.SoilType,
                    IrrigationSource = model.IrrigationSource
                });
                break;
            case UserType.Broker:
                _db.BrokerProfiles.Add(new BrokerProfile
                {
                    UserId = user.Id,
                    LicenseNumber = model.LicenseNumber,
                    CommissionRate = model.CommissionRate ?? 2.5m,
                    CoverageArea = model.Region
                });
                break;
            case UserType.Company:
                _db.CompanyProfiles.Add(new CompanyProfile
                {
                    UserId = user.Id,
                    CompanyName = model.CompanyName!,
                    CommercialRegistry = model.CommercialRegistry,
                    ActivityType = model.ActivityType
                });
                break;
        }

        _db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Title = "مرحباً بك في منصة ساوم",
            Body = "تم تفعيل حسابك. أكمل بيانات ملفك لرفع فرص المطابقة الذكية.",
            Url = "/Account/Profile"
        });
        await _db.SaveChangesAsync();

        await _signIn.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "تم إنشاء الحساب بنجاح.";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _db.Users
            .Include(u => u.FarmerProfile)
            .Include(u => u.BrokerProfile)
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == _users.GetUserId(User));

        if (user is null) return NotFound();
        return View(user);
    }

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string fullName, string? region, string? address,
        decimal? farmArea, string? soilType, string? irrigationSource, string? farmLocation, int? experienceYears,
        string? licenseNumber, decimal? commissionRate, string? coverageArea,
        string? companyName, string? commercialRegistry, string? activityType, decimal? monthlyDemand)
    {
        var userId = _users.GetUserId(User);
        var user = await _db.Users
            .Include(u => u.FarmerProfile)
            .Include(u => u.BrokerProfile)
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(fullName)) user.FullName = fullName.Trim();
        user.Region = region;
        user.Address = address;

        if (user.UserType == UserType.Farmer)
        {
            user.FarmerProfile ??= new FarmerProfile { UserId = user.Id };
            user.FarmerProfile.FarmArea = farmArea ?? 0;
            user.FarmerProfile.SoilType = soilType;
            user.FarmerProfile.IrrigationSource = irrigationSource;
            user.FarmerProfile.FarmLocation = farmLocation;
            user.FarmerProfile.ExperienceYears = experienceYears ?? 0;
        }
        else if (user.UserType == UserType.Broker)
        {
            user.BrokerProfile ??= new BrokerProfile { UserId = user.Id };
            user.BrokerProfile.LicenseNumber = licenseNumber;
            user.BrokerProfile.CommissionRate = Math.Clamp(commissionRate ?? 2.5m, 0m, 30m);
            user.BrokerProfile.CoverageArea = coverageArea;
        }
        else if (user.UserType == UserType.Company)
        {
            user.CompanyProfile ??= new CompanyProfile { UserId = user.Id, CompanyName = companyName ?? user.FullName };
            if (!string.IsNullOrWhiteSpace(companyName)) user.CompanyProfile.CompanyName = companyName;
            user.CompanyProfile.CommercialRegistry = commercialRegistry;
            user.CompanyProfile.ActivityType = activityType;
            user.CompanyProfile.MonthlyDemand = monthlyDemand ?? 0;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الملف الشخصي.";
        return RedirectToAction(nameof(Profile));
    }

    private static string Translate(string code, string fallback) => code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => "هذا البريد الإلكتروني مسجّل مسبقاً.",
        "PasswordTooShort" => "كلمة المرور قصيرة جداً.",
        "PasswordRequiresDigit" => "كلمة المرور يجب أن تحتوي رقماً.",
        _ => fallback
    };
}
