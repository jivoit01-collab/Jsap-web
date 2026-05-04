using JSAPNEW.Services.Implementation;
using JSAPNEW.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace JSAPNEW.Controllers
{
    [Authorize]
    public class QcWebController : Controller
    {
        private readonly IPrdoService _PrdoService;
        private readonly IInventoryAuditService _inventoryAuditService;
        private readonly IUserService _userService;
        public QcWebController(IPrdoService PrdoService, IInventoryAuditService inventoryAuditService, IUserService userService)
        {
            _PrdoService = PrdoService;
            _inventoryAuditService = inventoryAuditService;
            _userService = userService;
        }

        private async Task<bool> PrepareQualityCheckViewAsync()
        {
            var userId = HttpContext.Session.GetInt32("userId");
            var selectedCompanyId = HttpContext.Session.GetInt32("selectedCompanyId");

            if (!userId.HasValue || userId.Value <= 0 || !selectedCompanyId.HasValue || selectedCompanyId.Value <= 0)
            {
                return false;
            }

            ViewBag.UserId = userId.Value;
            ViewBag.userId = userId.Value;
            ViewBag.CompanyId = selectedCompanyId.Value;
            ViewBag.Company = selectedCompanyId.Value;

            var uniqueUsernameResponse = await _inventoryAuditService.GenerateUniqueUsernameAsync(userId.Value);
            ViewBag.LotNumber = uniqueUsernameResponse.Success && !string.IsNullOrWhiteSpace(uniqueUsernameResponse.UniqueUsername)
                ? uniqueUsernameResponse.UniqueUsername
                : $"QC-{userId.Value}-{DateTime.Now:yyyyMMddHHmmss}";

            return true;
        }

        public async Task<IActionResult> QualityCheck()
        {
            if (!await PrepareQualityCheckViewAsync())
                return RedirectToAction("Index", "Login");

            return View();
        }

        public async Task<IActionResult> AddQC()
        {
            if (!await PrepareQualityCheckViewAsync())
                return RedirectToAction("Index", "Login");

            return View();
        }

        public async Task<IActionResult> Index()
        {
            return RedirectToAction(nameof(QualityCheck));
        }
    }
}
