using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>استوديو المظهر — يضبط الأدمن ألوان المنصة وزواياها وخلفيتها وصورة الهيدر.</summary>
[Authorize(Roles = Roles.Admin)]
public class ThemeController : Controller
{
    private readonly ThemeService _theme;
    public ThemeController(ThemeService theme) => _theme = theme;

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _theme.GetAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ThemeSettings model)
    {
        await _theme.SaveAsync(model);
        TempData["Success"] = "تم حفظ المظهر وتطبيقه على المنصة.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        await _theme.ResetAsync();
        TempData["Success"] = "تمت إعادة المظهر إلى الوضع الافتراضي.";
        return RedirectToAction(nameof(Index));
    }
}
