using Microsoft.AspNetCore.Html;
using Sawm.Web.Models;

namespace Sawm.Web.Services;

/// <summary>
/// نظام أيقونات SVG مضمّن (نمط Lucide الخطي) — يستبدل الإيموجي بأيقونات
/// متسقة قابلة للتلوين عبر currentColor وتتوسّع دون فقدان الحدة.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        // التنقل
        ["dashboard"] = "<rect x='3' y='3' width='7' height='9' rx='1'/><rect x='14' y='3' width='7' height='5' rx='1'/><rect x='14' y='12' width='7' height='9' rx='1'/><rect x='3' y='16' width='7' height='5' rx='1'/>",
        ["gavel"] = "<path d='m14.5 12.5-8 8a2.12 2.12 0 1 1-3-3l8-8'/><path d='M17.5 15 9 6.5'/><path d='m14 6 6 6'/><path d='m11 3 6 6'/><path d='M5 21h14'/>",
        ["clipboard"] = "<rect x='8' y='2' width='8' height='4' rx='1'/><path d='M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2'/><path d='M9 12h6'/><path d='M9 16h4'/>",
        ["file-text"] = "<path d='M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z'/><path d='M14 2v5h6'/><path d='M8 13h8'/><path d='M8 17h5'/>",
        ["truck"] = "<path d='M14 18V6a1 1 0 0 0-1-1H2a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h1'/><path d='M14 9h4l3 3v5a1 1 0 0 1-1 1h-1'/><circle cx='6.5' cy='18' r='2'/><circle cx='17.5' cy='18' r='2'/>",
        ["handshake"] = "<path d='m11 17 2 2a1 1 0 1 0 3-3'/><path d='m14 14 2.5 2.5a1 1 0 1 0 3-3l-3.9-3.9a2 2 0 0 1 0-2.8l.8-.8a2 2 0 0 1 2.8 0L21 8'/><path d='m21 3-3 3'/><path d='M3 8l2.6-2.6a2 2 0 0 1 2.8 0l4 4a2 2 0 0 1 0 2.8L10 15'/>",
        ["settings"] = "<path d='M12.2 2h-.4a2 2 0 0 0-2 2v.2a2 2 0 0 1-1 1.7l-.4.2a2 2 0 0 1-2 0l-.2-.1a2 2 0 0 0-2.7.7l-.2.4a2 2 0 0 0 .7 2.7l.2.1a2 2 0 0 1 1 1.7v.5a2 2 0 0 1-1 1.7l-.2.1a2 2 0 0 0-.7 2.7l.2.4a2 2 0 0 0 2.7.7l.2-.1a2 2 0 0 1 2 0l.4.2a2 2 0 0 1 1 1.7V20a2 2 0 0 0 2 2h.4a2 2 0 0 0 2-2v-.2a2 2 0 0 1 1-1.7l.4-.2a2 2 0 0 1 2 0l.2.1a2 2 0 0 0 2.7-.7l.2-.4a2 2 0 0 0-.7-2.7l-.2-.1a2 2 0 0 1-1-1.7v-.5a2 2 0 0 1 1-1.7l.2-.1a2 2 0 0 0 .7-2.7l-.2-.4a2 2 0 0 0-2.7-.7l-.2.1a2 2 0 0 1-2 0l-.4-.2a2 2 0 0 1-1-1.7V4a2 2 0 0 0-2-2z'/><circle cx='12' cy='12' r='3'/>",
        ["bell"] = "<path d='M10.3 21a1.94 1.94 0 0 0 3.4 0'/><path d='M21 19H3l1.6-2.4A6 6 0 0 0 5.6 13V10a6.4 6.4 0 0 1 12.8 0v3a6 6 0 0 0 1 3.6L21 19z'/>",
        ["mail"] = "<rect x='2' y='4' width='20' height='16' rx='2'/><path d='m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7'/>",
        ["send"] = "<path d='M14.5 9.5 21 3m0 0-6.5 18a.55.55 0 0 1-1 0L10 14l-7-3.5a.55.55 0 0 1 0-1L21 3'/>",
        ["trash"] = "<path d='M3 6h18'/><path d='M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/>",
        ["users"] = "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.9'/><path d='M16 3.1a4 4 0 0 1 0 7.8'/>",
        ["user"] = "<path d='M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2'/><circle cx='12' cy='7' r='4'/>",
        ["logout"] = "<path d='M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4'/><path d='m16 17 5-5-5-5'/><path d='M21 12H9'/>",
        ["search"] = "<circle cx='11' cy='11' r='8'/><path d='m21 21-4.3-4.3'/>",
        ["menu"] = "<path d='M4 6h16'/><path d='M4 12h16'/><path d='M4 18h16'/>",

        // القطاع الزراعي
        ["wheat"] = "<path d='M2 22 16 8'/><path d='M3.47 12.53 5 11l1.53 1.53a3.5 3.5 0 0 1 0 4.94L5 19l-1.53-1.53a3.5 3.5 0 0 1 0-4.94Z'/><path d='M7.47 8.53 9 7l1.53 1.53a3.5 3.5 0 0 1 0 4.94L9 15l-1.53-1.53a3.5 3.5 0 0 1 0-4.94Z'/><path d='M11.47 4.53 13 3l1.53 1.53a3.5 3.5 0 0 1 0 4.94L13 11l-1.53-1.53a3.5 3.5 0 0 1 0-4.94Z'/>",
        ["building"] = "<rect x='4' y='2' width='16' height='20' rx='2'/><path d='M9 22v-4h6v4'/><path d='M8 6h.01'/><path d='M16 6h.01'/><path d='M12 6h.01'/><path d='M8 10h.01'/><path d='M16 10h.01'/><path d='M12 10h.01'/><path d='M8 14h.01'/><path d='M16 14h.01'/><path d='M12 14h.01'/>",

        // المال والحالة
        ["wallet"] = "<path d='M19 7V4a1 1 0 0 0-1-1H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v4h-3a2 2 0 0 0 0 4h3a1 1 0 0 0 1-1v-2a1 1 0 0 0-1-1'/><path d='M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1v-4'/>",
        ["shield-check"] = "<path d='M20 13c0 5-3.5 7.5-7.7 8.9a1 1 0 0 1-.6 0C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.2-2.7a1 1 0 0 1 1.5 0C14.5 3.8 17 5 19 5a1 1 0 0 1 1 1z'/><path d='m9 12 2 2 4-4'/>",
        ["check-circle"] = "<circle cx='12' cy='12' r='10'/><path d='m9 12 2 2 4-4'/>",
        ["clock"] = "<circle cx='12' cy='12' r='10'/><path d='M12 6v6l4 2'/>",
        ["alert"] = "<path d='m21.7 18-8-14a2 2 0 0 0-3.4 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3Z'/><path d='M12 9v4'/><path d='M12 17h.01'/>",
        ["snowflake"] = "<path d='M12 2v20'/><path d='m17 5-5 3-5-3'/><path d='m17 19-5-3-5 3'/><path d='M2.5 7.5 21.5 16.5'/><path d='m6 5.5.5 4-3.5 2'/><path d='m18 18.5-.5-4 3.5-2'/><path d='M21.5 7.5 2.5 16.5'/><path d='m18 5.5-.5 4 3.5 2'/><path d='m6 18.5.5-4-3.5-2'/>",
        ["star"] = "<path d='M11.5 3.2a.6.6 0 0 1 1 0l2.3 4.6a.6.6 0 0 0 .4.3l5.1.8a.6.6 0 0 1 .3 1l-3.7 3.6a.6.6 0 0 0-.1.5l.8 5a.6.6 0 0 1-.8.7l-4.5-2.4a.6.6 0 0 0-.6 0l-4.5 2.4a.6.6 0 0 1-.8-.7l.8-5a.6.6 0 0 0-.1-.5L3.4 9.9a.6.6 0 0 1 .3-1l5.1-.8a.6.6 0 0 0 .4-.3z'/>",
        ["trending-up"] = "<path d='M16 7h6v6'/><path d='m22 7-8.5 8.5-5-5L2 17'/>",
        ["trending-down"] = "<path d='M16 17h6v-6'/><path d='m22 17-8.5-8.5-5 5L2 7'/>",
        ["note"] = "<path d='M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7'/><path d='M18.4 2.6a2 2 0 0 1 3 3L16 11l-4 1 1-4Z'/>",
        ["signature"] = "<path d='M20 20H4'/><path d='M4 16c2-4 3-9 5-9s2 7 4 7 2-5 4-5 2 3 3 3'/>",
        ["package"] = "<path d='M11 21.7a2 2 0 0 0 2 0l7-4a2 2 0 0 0 1-1.7V8a2 2 0 0 0-1-1.7l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.7Z'/><path d='m3.3 7 8.7 5 8.7-5'/><path d='M12 22V12'/>",
        ["lock"] = "<rect x='3' y='11' width='18' height='11' rx='2'/><path d='M7 11V7a5 5 0 0 1 10 0v4'/>",
        ["plus"] = "<path d='M5 12h14'/><path d='M12 5v14'/>",
        ["check"] = "<path d='M20 6 9 17l-5-5'/>",
        ["x"] = "<path d='M18 6 6 18'/><path d='m6 6 12 12'/>",
        ["filter"] = "<path d='M3 6h18'/><path d='M7 12h10'/><path d='M10 18h4'/>",
        ["arrow-left"] = "<path d='m12 19-7-7 7-7'/><path d='M19 12H5'/>",
        ["chart"] = "<path d='M3 3v16a2 2 0 0 0 2 2h16'/><path d='M7 16v-4'/><path d='M12 16V8'/><path d='M17 16v-6'/>",
        ["seedling"] = "<path d='M7 20h10'/><path d='M12 20c0-6 0-8-3-10'/><path d='M12 14c0-4 2-6 6-6 0 4-2 6-6 6Z'/><path d='M12 12C12 9 10 7 6 7c0 3 2 5 6 5Z'/>",
        ["map-pin"] = "<path d='M20 10c0 4.4-5.6 9.5-7.4 11a1 1 0 0 1-1.2 0C9.6 19.5 4 14.4 4 10a8 8 0 0 1 16 0'/><circle cx='12' cy='10' r='3'/>",
        ["calendar"] = "<rect x='3' y='4' width='18' height='18' rx='2'/><path d='M16 2v4'/><path d='M8 2v4'/><path d='M3 10h18'/>",
    };

    /// <summary>يرجع أيقونة SVG جاهزة للإدراج داخل Razor</summary>
    public static IHtmlContent Render(string name, int size = 20, string? cssClass = null, double stroke = 1.75)
    {
        if (!Paths.TryGetValue(name, out var body))
            return HtmlString.Empty;

        var cls = string.IsNullOrWhiteSpace(cssClass) ? "icon" : $"icon {cssClass}";
        var svg =
            $"<svg class=\"{cls}\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" " +
            $"stroke=\"currentColor\" stroke-width=\"{stroke.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" " +
            $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">" +
            (body.StartsWith('<') ? body : $"<path d='{body}'/>") +
            "</svg>";

        return new HtmlString(svg.Replace('\'', '"'));
    }

    /// <summary>أيقونة الدور المناسبة لكل نوع مستخدم</summary>
    public static string ForUserType(UserType t) => t switch
    {
        UserType.Farmer => "wheat",
        UserType.Broker => "handshake",
        UserType.Company => "building",
        _ => "settings"
    };
}
