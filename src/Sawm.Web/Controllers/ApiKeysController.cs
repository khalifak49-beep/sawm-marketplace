using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;
using Sawm.Web.Services;

namespace Sawm.Web.Controllers;

/// <summary>إدارة مفاتيح API لربط الأنظمة الخارجية — إنشاء مفتاح وتحديد الموارد والحقول المسموح بها.</summary>
[Authorize(Roles = Roles.Admin)]
public class ApiKeysController : Controller
{
    private readonly SawmDbContext _db;
    public ApiKeysController(SawmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var keys = await _db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.CreatedAt).ToListAsync();
        return View(keys);
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? note, string[] fields)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "أدخل اسم النظام المستهلك.";
            return RedirectToAction(nameof(Create));
        }

        var fieldsJson = ApiCatalog.BuildFieldsJson(fields ?? Array.Empty<string>());
        var parsed = ApiCatalog.ParseFields(fieldsJson);
        if (parsed.Count == 0 || parsed.All(p => p.Value.Length == 0))
        {
            TempData["Error"] = "اختر موردًا واحدًا على الأقل وحقلًا واحدًا على الأقل منه.";
            return RedirectToAction(nameof(Create));
        }

        var (plain, prefix, hash) = ApiKeyService.Generate();
        _db.ApiKeys.Add(new ApiKey
        {
            Name = name,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            KeyPrefix = prefix,
            KeyHash = hash,
            FieldsJson = fieldsJson,
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        // المفتاح الكامل يظهر مرة واحدة فقط — لا يُخزَّن نصاً صريحاً
        TempData["NewApiKey"] = plain;
        TempData["NewApiKeyName"] = name;
        TempData["Success"] = "تم إنشاء مفتاح API. انسخه الآن — لن يظهر مرة أخرى.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var key = await _db.ApiKeys.FindAsync(id);
        if (key is not null)
        {
            key.IsActive = !key.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = key.IsActive ? "تم تفعيل المفتاح." : "تم تعطيل المفتاح.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var key = await _db.ApiKeys.FindAsync(id);
        if (key is not null)
        {
            _db.ApiKeys.Remove(key);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم حذف المفتاح نهائياً.";
        }
        return RedirectToAction(nameof(Index));
    }
}
