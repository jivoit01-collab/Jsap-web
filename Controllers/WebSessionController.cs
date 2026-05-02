using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using JSAPNEW.Data.Entities;
using JSAPNEW.Models;
using JSAPNEW.Services.Interfaces;
using System.Security.Claims;

namespace JSAPNEW.Controllers
{
    [Route("websession")]
    [Authorize]
    public class WebSessionController : Controller
    {
        private readonly IUserService _userService;

        public WebSessionController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost("set")]
        public async Task<IActionResult> SetSession([FromBody] SessionRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("userId");
                if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
                {
                    return Unauthorized(new { success = false, message = "Authentication required" });
                }

                // Fetch companies using the existing service
                var companies = (await _userService.GetCompanyAsync(userId))?.ToList();

                if (companies == null || companies.Count == 0)
                {
                    return BadRequest(new { success = false, message = "No companies found for user" });
                }

                // Set session values
                HttpContext.Session.SetInt32("userId", userId);
                HttpContext.Session.SetString("username", User.Identity?.Name ?? request?.UserName ?? "Guest");
                HttpContext.Session.SetString("companyList", JsonConvert.SerializeObject(companies));
                HttpContext.Session.SetInt32("selectedCompanyId", companies[0].id);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SetSession: {ex.Message}");
                return StatusCode(500, new { success = false, message = "An error occurred while setting session data" });
            }
        }

        [HttpPost("updateSelectedCompany")]
        public IActionResult UpdateSelectedCompany([FromBody] CompanySelectRequest req)
        {
            try
            {
                if (req == null || req.CompanyId <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid company ID" });
                }

                var companyListJson = HttpContext.Session.GetString("companyList");
                var companies = string.IsNullOrWhiteSpace(companyListJson)
                    ? new List<CompanyModel>()
                    : JsonConvert.DeserializeObject<List<CompanyModel>>(companyListJson) ?? new List<CompanyModel>();

                if (!companies.Any(c => c.id == req.CompanyId))
                {
                    return Forbid();
                }

                HttpContext.Session.SetInt32("selectedCompanyId", req.CompanyId);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateSelectedCompany: {ex.Message}");
                return StatusCode(500, new { success = false, message = "An error occurred while updating company selection" });
            }
        }

        // Check if user is authenticated via cookie
        [HttpGet("checksession")]
        public IActionResult CheckSession()
        {
            try
            {
                // Check both cookie auth and session
                var userId = HttpContext.Session.GetInt32("userId");
                var companyList = HttpContext.Session.GetString("companyList");

                // Also check if user is authenticated via cookie
                if (User.Identity?.IsAuthenticated == true)
                {
                    return Json(new { valid = true, userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value });
                }

                if (!userId.HasValue || string.IsNullOrEmpty(companyList))
                {
                    return Json(new { valid = false });
                }

                return Json(new { valid = true });
            }
            catch (Exception)
            {
                return Json(new { valid = false });
            }
        }
    }

    public class CompanySelectRequest
    {
        public int CompanyId { get; set; }
    }

    public class SessionRequest
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
    }

    public class CompanyDto
    {
        public int id { get; set; }
        public string company { get; set; }
    }
}
