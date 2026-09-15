using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sawm.Web.Data;
using Sawm.Web.Models;

namespace Sawm.Web.Services;

/// <summary>توليد مفاتيح API وتجزئتها والمصادقة عليها. المفتاح الكامل يظهر مرة واحدة فقط عند الإنشاء.</summary>
public class ApiKeyService
{
    private readonly SawmDbContext _db;
    public ApiKeyService(SawmDbContext db) => _db = db;

    /// <summary>يولّد مفتاحاً جديداً ويعيد (النص الصريح، البادئة للعرض، التجزئة للتخزين).</summary>
    public static (string plain, string prefix, string hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(bytes)
            .Replace("+", "").Replace("/", "").Replace("=", "");
        var plain = "sawm_" + body;
        var prefix = plain[..Math.Min(12, plain.Length)] + "…";
        return (plain, prefix, Hash(plain));
    }

    public static string Hash(string plain)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>يتحقق من مفتاح واردٍ ويحدّث آخر استخدام/العدّاد. يعيد null إذا كان غير صالح أو معطّلاً.</summary>
    public async Task<ApiKey?> AuthenticateAsync(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;
        var hash = Hash(plain.Trim());
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);
        if (key is null) return null;
        key.LastUsedAt = DateTime.Now;
        key.RequestCount++;
        await _db.SaveChangesAsync();
        return key;
    }
}
