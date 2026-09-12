using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>استوديو المظهر — يضبط الأدمن ألوان المنصة وزواياها وخلفيتها وصورة الهيدر والشعار.</summary>
[Authorize(Roles = Roles.Admin)]
public class ThemeController : Controller
{
    private readonly ThemeService _theme;
    public ThemeController(ThemeService theme) => _theme = theme;

    private static readonly string[] AllowedTypes =
        { "image/png", "image/jpeg", "image/jpg", "image/webp", "image/gif", "image/svg+xml" };
    private const long MaxBytes = 4 * 1024 * 1024; // 4MB

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _theme.GetAsync());

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Save(ThemeSettings model, IFormFile? headerImageFile, IFormFile? emblemImageFile)
    {
        var (hd, ht, he) = await ReadImageAsync(headerImageFile);
        var (ed, et, ee) = await ReadImageAsync(emblemImageFile);
        if (he is not null || ee is not null)
        {
            TempData["Error"] = he ?? ee;
            return RedirectToAction(nameof(Index));
        }

        model.HeaderImageData = hd; model.HeaderImageType = ht;
        model.EmblemImageData = ed; model.EmblemImageType = et;

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

    /// <summary>يقدّم صورة الهيدر المرفوعة (مخزّنة في القاعدة). متاح للجميع لأنه يظهر في الصفحات العامة.</summary>
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> HeaderImage()
    {
        var t = await _theme.GetAsync();
        if (t.HeaderImageData is { Length: > 0 } && !string.IsNullOrEmpty(t.HeaderImageType))
        {
            Harden();
            return File(t.HeaderImageData, t.HeaderImageType);
        }
        return NotFound();
    }

    /// <summary>يقدّم صورة الشعار المرفوعة (مخزّنة في القاعدة).</summary>
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> EmblemImage()
    {
        var t = await _theme.GetAsync();
        if (t.EmblemImageData is { Length: > 0 } && !string.IsNullOrEmpty(t.EmblemImageType))
        {
            Harden();
            return File(t.EmblemImageData, t.EmblemImageType);
        }
        return NotFound();
    }

    /// <summary>تأمين استجابة الصورة: منع تخمين النوع وتعطيل أي سكربت داخل ملفات SVG.</summary>
    private void Harden()
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers["Cache-Control"] = "public, max-age=86400";
    }

    private static async Task<(byte[]? data, string? type, string? error)> ReadImageAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return (null, null, null);
        if (file.Length > MaxBytes) return (null, null, "حجم الصورة يتجاوز 4 ميجابايت.");
        var type = file.ContentType?.ToLowerInvariant();
        if (type is null || Array.IndexOf(AllowedTypes, type) < 0)
            return (null, null, "صيغة الصورة غير مدعومة (المسموح: PNG أو JPG أو WEBP أو GIF أو SVG).");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return (ms.ToArray(), type == "image/jpg" ? "image/jpeg" : type, null);
    }
}
