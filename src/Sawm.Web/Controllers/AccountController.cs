using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
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
    private readonly EmailQueue _emails;

    public AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        SawmDbContext db, EmailQueue emails)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _emails = emails;
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

        if (result.IsNotAllowed)
        {
            // الأرجح: البريد لم يُؤكَّد بعد — نوجّه لإعادة إرسال رابط التفعيل
            ModelState.AddModelError(string.Empty, "لم يتم تفعيل بريدك الإلكتروني بعد. تحقّق من صندوق الوارد أو أعد إرسال رابط التفعيل.");
            ViewBag.ResendEmail = model.Email;
            return View(model);
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
            EmailConfirmed = false, // يتأكّد عبر رابط التفعيل المُرسَل بالبريد
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
            Body = "أكمل بيانات ملفك لرفع فرص المطابقة الذكية.",
            Url = "/Account/Profile"
        });
        await _db.SaveChangesAsync();

        // إرسال رابط تفعيل البريد — لا يُسجّل الدخول قبل التأكيد
        await SendConfirmationEmailAsync(user);
        TempData["Success"] = "تم إنشاء الحساب. أرسلنا رابط تفعيل إلى بريدك.";
        return RedirectToAction(nameof(RegisterConfirmation), new { email = user.Email });
    }

    /// <summary>صفحة "تحقّق من بريدك" بعد التسجيل</summary>
    [HttpGet]
    public IActionResult RegisterConfirmation(string? email)
    {
        ViewBag.Email = email;
        return View();
    }

    /// <summary>تأكيد البريد عبر الرابط المُرسَل</summary>
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
        {
            ViewBag.Ok = false;
            return View();
        }

        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            ViewBag.Ok = false;
            return View();
        }

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch { ViewBag.Ok = false; return View(); }

        var result = await _users.ConfirmEmailAsync(user, token);
        ViewBag.Ok = result.Succeeded;
        return View();
    }

    /// <summary>إعادة إرسال رابط التفعيل</summary>
    [HttpGet]
    public IActionResult ResendConfirmation(string? email)
    {
        ViewBag.Email = email;
        return View();
    }

    [HttpPost, ActionName("ResendConfirmation"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmationPost(string email)
    {
        // رسالة موحّدة دائماً — لا نكشف إن كان البريد مسجّلاً أم لا
        TempData["Success"] = "إن كان البريد مسجّلاً وغير مفعّل، فقد أرسلنا إليه رابط تفعيل جديد.";
        var user = string.IsNullOrWhiteSpace(email) ? null : await _users.FindByEmailAsync(email);
        if (user is not null && !await _users.IsEmailConfirmedAsync(user))
            await SendConfirmationEmailAsync(user);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>يولّد رمز التأكيد ويُدرج بريد التفعيل في الطابور</summary>
    private async Task SendConfirmationEmailAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return;
        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Action(nameof(ConfirmEmail), "Account",
            new { userId = user.Id, code }, protocol: Request.Scheme)!;

        var body = $@"مرحباً {System.Net.WebUtility.HtmlEncode(user.FullName)}،<br><br>
            شكراً لتسجيلك في منصة ساوم. لتفعيل حسابك والبدء في استخدام المنصة، اضغط الزر أدناه.
            <br><br>إن لم تُنشئ هذا الحساب فتجاهل هذه الرسالة.";
        _emails.Enqueue(new EmailMessage(
            new[] { user.Email! },
            "ساوم — تفعيل حسابك",
            EmailTemplate.Wrap("تفعيل حسابك في منصة ساوم", body, link, "تفعيل الحساب")));
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
