using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace JSAPNEW.Controllers
{
    [AllowAnonymous]
    public class LoginController : Controller
    {
        private const string AuthenticatedLandingPage = "/DashboardWeb/Index";

        public IActionResult Index()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            if (User?.Identity?.IsAuthenticated == true)
            {
                return Redirect(AuthenticatedLandingPage);
            }

            return View();
        }
    }
}
