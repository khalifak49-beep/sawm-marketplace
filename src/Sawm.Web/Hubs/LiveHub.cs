using Microsoft.AspNetCore.SignalR;

namespace Sawm.Web.Hubs;

/// <summary>
/// قناة البث اللحظي (SignalR). الرسائل تُرسَل من الخادم:
///  • "notify"      → إشعار شخصي لمستخدم بعينه (جرس + توست)
///  • "dataChanged" → تغيّر بيانات ضمن فئة (auctions/contracts/tenders/logistics) لتحديث الشاشات المفتوحة
/// الاتصال متاح للجميع (زوّار وصفحات عامة)؛ الرسائل الشخصية تُوجَّه عبر هوية المستخدم.
/// </summary>
public class LiveHub : Hub
{
}
