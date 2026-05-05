using JSAPNEW.Models;
using JSAPNEW.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.RateLimiting;
using Newtonsoft.Json;

namespace JSAPNEW.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private const string RefreshTokenCookieName = "JSAP.Refresh";
        private static readonly string[] LegacyRefreshTokenCookieNames = { "refreshToken", "refresh_token" };
        private const string RefreshTokenCookiePath = "/api/Auth";
        private readonly IUserService _userService;
        private readonly IAuthSecurityService _authSecurityService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserService userService, IAuthSecurityService authSecurityService, ILogger<AuthController> logger)
        {
            _userService = userService;
            _authSecurityService = authSecurityService;
            _logger = logger;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Invalid request data" });

            var result = await _userService.ValidateUserAsync(request);

            if (!result.Success || result.User == null)
            {
                _logger.LogWarning("Failed login attempt for user: {User}", request.loginUser);
                return Unauthorized(new { success = false, message = "Invalid credentials" });
            }

            var user = result.User;

            var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role.Trim();
            await SignInCookieAsync(user, role);

            var companies = (await _userService.GetCompanyAsync(user.userId))?.ToList() ?? new List<CompanyModel>();
            HttpContext.Session.SetInt32("userId", user.userId);
            HttpContext.Session.SetString("username", user.loginUser ?? "Guest");
            HttpContext.Session.SetString("userName", user.loginUser ?? "Guest");
            HttpContext.Session.SetString("loginUser", user.loginUser ?? "Guest");
            HttpContext.Session.SetString("userEmail", user.userEmail ?? "");
            HttpContext.Session.SetString("companyList", JsonConvert.SerializeObject(companies));
            if (companies.Count > 0)
            {
                HttpContext.Session.SetInt32("selectedCompanyId", companies[0].id);
            }

            var refreshToken = await _authSecurityService.GenerateRefreshTokenAsync(
                user.userId,
                GetClientIpAddress(),
                GetUserAgent(),
                role);
            SetRefreshTokenCookie(refreshToken);

            _logger.LogInformation("Successful login for user: {User}", request.loginUser);

            return Ok(new
            {
                success = true,
                message = "Login successful",
                user = new
                {
                    userId = user.userId,
                    userName = user.loginUser,
                    userEmail = user.userEmail,
                    firstName = user.firstName,
                    lastName = user.lastName
                }
            });
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request)
        {
            Request.Cookies.TryGetValue(RefreshTokenCookieName, out var oldRefreshToken);

            if (string.IsNullOrWhiteSpace(oldRefreshToken))
                return Unauthorized(new { success = false, message = "Refresh token required" });

            var validation = await _authSecurityService.ValidateRefreshTokenAsync(
                oldRefreshToken,
                GetClientIpAddress(),
                GetUserAgent());

            if (!validation.IsValid || validation.UserId <= 0)
            {
                DeleteAllRefreshTokenCookies();
                return Unauthorized(new { success = false, message = "Invalid refresh token" });
            }

            var newRefreshToken = await _authSecurityService.GenerateRefreshTokenAsync(
                validation.UserId,
                GetClientIpAddress(),
                GetUserAgent(),
                validation.Role);

            SetRefreshTokenCookie(newRefreshToken);
            await SignInCookieAsync(new UserDto
            {
                userId = validation.UserId,
                loginUser = validation.UserId.ToString(),
                Role = validation.Role
            }, validation.Role);

            return Ok(new
            {
                success = true,
                expiresUtc = DateTime.UtcNow.AddMinutes(15)
            });
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody] RevokeTokenRequest? request)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            DeleteAllRefreshTokenCookies();
            return Ok(new { success = true, message = "Logged out successfully" });
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            try
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("userId");
                if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
                    return Unauthorized(new { success = false, message = "Authentication required" });

                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                    return NotFound(new { success = false, message = "User not found" });

                var displayName = BuildDisplayName(user);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        userId = user.userId,
                        userName = displayName,
                        loginUser = user.loginUser,
                        userEmail = user.userEmail
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching current user profile");
                return StatusCode(500, new { success = false, message = "Something went wrong" });
            }
        }

        [HttpGet("getcompanies")]
        public async Task<IActionResult> GetCompanies()
        {
            try
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("userId");
                if (!int.TryParse(userIdClaim, out var userId) || userId <= 0)
                    return Unauthorized(new { success = false, message = "Authentication required" });

                if (userId <= 0)
                    return BadRequest(new { success = false, message = "Invalid user ID" });

                var companies = await _userService.GetCompanyAsync(userId);
                var companyList = companies?.ToList() ?? new List<CompanyModel>();

                if (companyList.Count == 0)
                    return Ok(new { success = false, message = "No companies found for this user", data = companyList });

            return Ok(new { success = true, data = companyList });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching companies for authenticated user");
                return StatusCode(500, new { success = false, message = "Something went wrong" });
            }
        }

        [HttpGet("GetAllBudgetInsight")]
        public async Task<IActionResult> GetBudgetInsight(int company, string month)
        {
            try
            {
                if (company <= 0)
                    return BadRequest(new { success = false, message = "Company is required" });

                var data = (await _userService.GetBudgetInsightAsync(company, month))?.ToList() ?? new List<GetAllBudgetInsight>();
                if (data.Count == 0)
                    return Ok(new { success = true, data });

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching budget insight for reports.");
                return StatusCode(500, new { success = false, message = "An error occurred while loading budget insight" });
            }
        }

        [HttpGet("GetAllBudgetSummaryAmount")]
        public async Task<IActionResult> GetAllBudgetSummaryAmount(int company, string month)
        {
            try
            {
                if (company <= 0)
                    return BadRequest(new { success = false, message = "Company is required" });

                var insightData = (await _userService.GetBudgetInsightAsync(company, month))?.ToList() ?? new List<GetAllBudgetInsight>();
                var finalResult = new List<object>();

                foreach (var user in insightData)
                {
                    var combinedBudget = await _userService.GetCombinedBudgetsAsync(user.UserID, company, month);
                    var budgetArray = combinedBudget.BudgetData.Select(b =>
                    {
                        var detail = combinedBudget.BudgetDetails.FirstOrDefault(x => x.BudgetHeader?.BudgetId == b.BudgetId);

                        return new
                        {
                            budgetId = b.BudgetId,
                            objType = b.objType,
                            company = b.company,
                            docEntry = b.DocEntry,
                            objectName = b.ObjectName,
                            cardCode = b.CardCode,
                            cardName = b.CardName,
                            docDate = b.DocDate?.ToString(),
                            totalAmount = b.TotalAmount,
                            status = b.Status,
                            header = new
                            {
                                templateId = detail?.BudgetHeader?.TemplateId,
                                totalStage = detail?.BudgetHeader?.TotalStage,
                                currentStageId = detail?.BudgetHeader?.CurrentStageId,
                                currentStatus = detail?.BudgetHeader?.CurrentStatus
                            },
                            lines = detail?.BudgetLines?.Select(line => new
                            {
                                budget = line.Budget,
                                subBudget = line.SubBudget,
                                variety = line.variety,
                                acctCode = line.AcctCode,
                                acctName = line.AcctName,
                                lineNum = line.LineNum,
                                amount = line.Amount,
                                state = line.State,
                                effectMonth = line.EffectMonth,
                                lineRemarks = line.LineRemarks,
                                comments = line.Comments
                            }).ToList()
                        };
                    }).ToList();

                    finalResult.Add(new
                    {
                        userId = user.UserID,
                        userName = user.UserName,
                        budget = budgetArray
                    });
                }

                return Ok(new { success = true, data = finalResult });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching budget summary amount for reports.");
                return StatusCode(500, new { success = false, message = "An error occurred while loading budget summary" });
            }
        }

        [HttpGet("getpendingbudgetwithdetails")]
        public async Task<IActionResult> GetPendingBudgetWithDetails([FromQuery(Name = "userId")] int reportUserId, int company, string month)
        {
            return await GetBudgetDetailsForReport(
                reportUserId,
                company,
                month,
                () => _userService.GetPendingBudgetWithDetailsAsync(reportUserId, company, month));
        }

        [HttpGet("getapprovedbudgetwithdetails")]
        public async Task<IActionResult> GetApprovedBudgetWithDetails([FromQuery(Name = "userId")] int reportUserId, int company, string month)
        {
            return await GetBudgetDetailsForReport(
                reportUserId,
                company,
                month,
                () => _userService.GetApprovedBudgetWithDetailsAsync(reportUserId, company, month));
        }

        [HttpGet("getrejectedbudgetwithdetails")]
        public async Task<IActionResult> GetRejectedBudgetWithDetails([FromQuery(Name = "userId")] int reportUserId, int company, string month)
        {
            return await GetBudgetDetailsForReport(
                reportUserId,
                company,
                month,
                () => _userService.GetRejectedBudgetWithDetailsAsync(reportUserId, company, month));
        }

        [HttpGet("GetBudgetApprovalFlow")]
        public async Task<IActionResult> GetBudgetApprovalFlow(int budgetId)
        {
            try
            {
                if (budgetId <= 0)
                    return BadRequest(new { success = false, message = "Budget ID is required" });

                var data = (await _userService.GetBudgetApprovalFlowAsync(budgetId))?.ToList() ?? new List<BudgetApprovalFlowModel>();
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching budget approval flow.");
                return StatusCode(500, new { success = false, message = "An error occurred while loading approval flow" });
            }
        }

        private async Task<IActionResult> GetBudgetDetailsForReport<TBudget>(
            int reportUserId,
            int company,
            string month,
            Func<Task<IEnumerable<TBudget>>> loader)
        {
            try
            {
                if (reportUserId <= 0 || company <= 0)
                    return BadRequest(new { success = false, message = "User and company are required" });

                var data = (await loader())?.ToList() ?? new List<TBudget>();
                foreach (var budget in data)
                {
                    var budgetIdProperty = budget?.GetType().GetProperty("BudgetId");
                    var nextApproverProperty = budget?.GetType().GetProperty("NextApprover");
                    if (budgetIdProperty == null || nextApproverProperty == null)
                        continue;

                    var budgetIdValue = budgetIdProperty.GetValue(budget);
                    if (budgetIdValue is int budgetId && budgetId > 0)
                    {
                        var nextApprovers = await _userService.GetNextApproverAsync(budgetId);
                        nextApproverProperty.SetValue(budget, nextApprovers);
                    }
                }

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching budget details for reports.");
                return StatusCode(500, new { success = false, message = "An error occurred while loading budget details" });
            }
        }

        private void SetRefreshTokenCookie(string refreshToken)
        {
            DeleteAllRefreshTokenCookies();
            var options = BuildRefreshTokenCookieOptions();
            options.Expires = DateTimeOffset.UtcNow.AddDays(7);
            Response.Cookies.Append(RefreshTokenCookieName, refreshToken, options);
        }

        private void DeleteAllRefreshTokenCookies()
        {
            DeleteRefreshTokenCookie(RefreshTokenCookieName);

            foreach (var legacyCookieName in LegacyRefreshTokenCookieNames)
            {
                DeleteRefreshTokenCookie(legacyCookieName);
            }
        }

        private void DeleteRefreshTokenCookie(string cookieName)
        {
            Response.Cookies.Delete(cookieName, BuildRefreshTokenCookieOptions());
            Response.Cookies.Delete(cookieName, BuildRefreshTokenCookieOptions("/"));
        }

        private CookieOptions BuildRefreshTokenCookieOptions(string? path = null)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = path ?? RefreshTokenCookiePath
            };
        }

        private async Task SignInCookieAsync(UserDto user, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.userId.ToString()),
                new Claim("userId", user.userId.ToString()),
                new Claim(ClaimTypes.Name, user.loginUser ?? ""),
                new Claim(ClaimTypes.Email, user.userEmail ?? ""),
                new Claim("userEmail", user.userEmail ?? ""),
                new Claim("FirstName", user.firstName ?? ""),
                new Claim("LastName", user.lastName ?? ""),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }

        private string GetClientIpAddress()
            => HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        private string GetUserAgent()
            => Request.Headers.UserAgent.ToString();

        private static string BuildDisplayName(UserDto user)
        {
            var fullName = $"{user.firstName} {user.lastName}".Trim();
            if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
            if (!string.IsNullOrWhiteSpace(user.userName)) return user.userName;
            if (!string.IsNullOrWhiteSpace(user.loginUser)) return user.loginUser;
            return user.userId.ToString();
        }
    }
}
