using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;

namespace Sawm.Web.Services;

/// <summary>إعدادات المظهر: قراءة الصف الوحيد (مع تخزين مؤقت) وبناء متغيّرات CSS لتطبيقها على كل الصفحات.</summary>
public class ThemeService
{
    private static ThemeSettings? _cache;
    private readonly SawmDbContext _db;
    public ThemeService(SawmDbContext db) => _db = db;

    public async Task<ThemeSettings> GetAsync()
    {
        if (_cache is not null) return _cache;
        var t = await _db.ThemeSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
        if (t is null)
        {
            t = new ThemeSettings { Id = 1 };
            _db.ThemeSettings.Add(t);
            await _db.SaveChangesAsync();
        }
        _cache = t;
        return t;
    }

    public async Task SaveAsync(ThemeSettings s)
    {
        var t = await _db.ThemeSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (t is null) { t = new ThemeSettings { Id = 1 }; _db.ThemeSettings.Add(t); }

        t.Primary = Color(s.Primary, "#15803D");
        t.Secondary = Color(s.Secondary, "#22C55E");
        t.Accent = Color(s.Accent, "#A16207");
        t.RadiusCard = Clamp(s.RadiusCard, 0, 40);
        t.RadiusButton = Clamp(s.RadiusButton, 0, 40);
        t.RadiusIcon = Clamp(s.RadiusIcon, 0, 40);
        t.RadiusControl = Clamp(s.RadiusControl, 0, 40);
        t.IconScale = Clamp(s.IconScale, 70, 160);
        t.GlassOpacity = Clamp(s.GlassOpacity, 25, 95);
        t.GlassBlur = Clamp(s.GlassBlur, 0, 30);
        ApplyImage(s.HeaderImageData, s.HeaderImageType, s.HeaderImageUrl, "/Theme/HeaderImage",
                   d => t.HeaderImageData = d, ty => t.HeaderImageType = ty, u => t.HeaderImageUrl = u,
                   () => t.HeaderImageData);
        ApplyImage(s.EmblemImageData, s.EmblemImageType, s.EmblemImageUrl, "/Theme/EmblemImage",
                   d => t.EmblemImageData = d, ty => t.EmblemImageType = ty, u => t.EmblemImageUrl = u,
                   () => t.EmblemImageData);
        t.NationalMode = s.NationalMode;
        t.BgEffect = s.BgEffect is "mesh" or "drift" or "meteors" or "bubbles" or "none" ? s.BgEffect : "mesh";
        t.FxShape = s.FxShape is "streak" or "dot" or "star" ? s.FxShape : "streak";
        t.FxColor = Color(s.FxColor, "#86EFAC");
        t.FxSize = Clamp(s.FxSize, 20, 180);
        t.FxCount = Clamp(s.FxCount, 3, 45);
        t.FxSpeed = Clamp(s.FxSpeed, 3, 24);

        await _db.SaveChangesAsync();
        _cache = null; // إبطال المؤقت — يُعاد التحميل في الطلب التالي
    }

    public async Task ResetAsync() => await SaveAsync(new ThemeSettings());

    /// <summary>يبني كتلة CSS تُحقن في كل صفحة لتجاوز متغيّرات المظهر.</summary>
    public static string BuildCss(ThemeSettings t)
    {
        var inv = CultureInfo.InvariantCulture;
        var css =
            ":root{" +
            $"--primary:{t.Primary};--secondary:{t.Secondary};--accent:{t.Accent};" +
            $"--r-card:{t.RadiusCard}px;--r-btn:{t.RadiusButton}px;--r-icon:{t.RadiusIcon}px;--r-control:{t.RadiusControl}px;" +
            $"--icon-scale:{(t.IconScale / 100.0).ToString("0.##", inv)};" +
            $"--glass-opacity:{(t.GlassOpacity / 100.0).ToString("0.##", inv)};" +
            $"--glass-blur-amount:{t.GlassBlur}px;" +
            $"--fx-color:{t.FxColor};--fx-size:{t.FxSize}px;--fx-dur:{t.FxSpeed}s;" +
            "}";

        if (!string.IsNullOrWhiteSpace(t.HeaderImageUrl))
            css += ".topbar{background-image:linear-gradient(rgba(255,255,255,.55),rgba(255,255,255,.72))," +
                   $"url('{t.HeaderImageUrl}');background-size:cover;background-position:center;}}";

        return css;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    private static string Color(string? v, string fallback)
    {
        v = (v ?? "").Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$") ? v : fallback;
    }

    /// <summary>يطبّق صورة على حقول المظهر: ملف مرفوع (يُخزَّن في القاعدة) أو رابط خارجي.
    /// عند وجود ملف جديد يُخزَّن ويصبح الرابط مساراً داخلياً؛ وإلا يُستخدم الرابط ويُمسح المخزَّن،
    /// مع الاحتفاظ بالصورة المخزّنة إن أبقى المستخدم المسار الداخلي دون رفع ملف جديد.</summary>
    private static void ApplyImage(
        byte[]? newData, string? newType, string? postedUrl, string internalRoute,
        Action<byte[]?> setData, Action<string?> setType, Action<string?> setUrl,
        Func<byte[]?> currentData)
    {
        if (newData is { Length: > 0 })
        {
            setData(newData);
            setType(newType);
            setUrl(internalRoute + "?v=" + DateTime.UtcNow.Ticks);
            return;
        }

        var url = SanitizeUrl(postedUrl);
        if (url != null && url.StartsWith(internalRoute, StringComparison.Ordinal))
        {
            // لم يُرفع ملف جديد والمستخدم أبقى المسار الداخلي → احتفظ بالصورة المخزّنة إن وُجدت
            var has = currentData() is { Length: > 0 };
            setUrl(has ? url : null);
            if (!has) { setData(null); setType(null); }
        }
        else
        {
            setUrl(url);       // رابط خارجي أو فارغ
            setData(null);
            setType(null);
        }
    }

    /// <summary>يقبل روابط http(s) أو مسارات داخلية فقط، ويزيل محارف قد تكسر CSS.</summary>
    private static string? SanitizeUrl(string? url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return null;
        if (url.Length > 500) url = url[..500];
        if (url.IndexOfAny(new[] { '\'', '"', '(', ')', '\n', '\r', ';', '<', '>' }) >= 0) return null;
        var ok = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              || url.StartsWith("/");
        return ok ? url : null;
    }
}
