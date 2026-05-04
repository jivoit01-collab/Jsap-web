using JSAPNEW.Services.Implementation;
using JSAPNEW.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

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
            var userId = GetUserId();
            var selectedCompanyId = await GetSelectedCompanyIdAsync(userId ?? 0);

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

        private int? GetUserId()
        {
            var sessionUserId = HttpContext.Session.GetInt32("userId");
            if (sessionUserId.HasValue && sessionUserId.Value > 0)
                return sessionUserId;

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("userId");

            if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
                return null;

            HttpContext.Session.SetInt32("userId", userId);
            return userId;
        }

        private async Task<int?> GetSelectedCompanyIdAsync(int userId)
        {
            var selectedCompanyId = HttpContext.Session.GetInt32("selectedCompanyId");
            if (selectedCompanyId.HasValue && selectedCompanyId.Value > 0)
                return selectedCompanyId;

            if (userId <= 0)
                return null;

            try
            {
                var companies = (await _userService.GetCompanyAsync(userId))?.ToList() ?? new();
                if (companies.Count == 0)
                    return null;

                HttpContext.Session.SetInt32("selectedCompanyId", companies[0].id);
                HttpContext.Session.SetString("companyList", Newtonsoft.Json.JsonConvert.SerializeObject(companies));
                return companies[0].id;
            }
            catch
            {
                return null;
            }
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
