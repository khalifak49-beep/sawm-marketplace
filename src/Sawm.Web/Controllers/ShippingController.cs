using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sawm.Web.Controllers;

/// <summary>
/// عرض تجريبي (واجهات فقط) لمنصة الشحن اللوجستي المنفصلة — بلا قاعدة بيانات ولا مصادقة حقيقية.
/// الغرض: استعراض الفكرة للعميل. الفصل الحقيقي وربط الـAPI مع ساوم يأتيان لاحقاً.
/// </summary>
[AllowAnonymous]
public class ShippingController : Controller
{
    public IActionResult Index() => View();
    public IActionResult Login() => View();
    public IActionResult Register() => View();
    public IActionResult Dashboard() => View();
}
