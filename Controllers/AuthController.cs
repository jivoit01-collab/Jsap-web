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
        private readonly ITokenService _tokenService;
        private readonly IAuthSecurityService _authSecurityService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserService userService, ITokenService tokenService, IAuthSecurityService authSecurityService, ILogger<AuthController> logger)
        {
            _userService = userService;
            _tokenService = tokenService;
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

            // Create claims for cookie authentication
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.userId.ToString()),
                new Claim("userId", user.userId.ToString()),
                new Claim(ClaimTypes.Name, user.loginUser ?? ""),
                new Claim(ClaimTypes.Email, user.userEmail ?? ""),
                new Claim("userEmail", user.userEmail ?? ""),
                new Claim("FirstName", user.firstName ?? ""),
                new Claim("LastName", user.lastName ?? "")
            };

            var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role.Trim();
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));

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
                accessToken = _tokenService.GenerateToken(user),
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

            var accessToken = _tokenService.GenerateToken(new UserDto
            {
                userId = validation.UserId,
                loginUser = validation.UserId.ToString(),
                Role = validation.Role
            });

            return Ok(new
            {
                success = true,
                accessToken,
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
