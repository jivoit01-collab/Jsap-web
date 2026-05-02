using JSAPNEW.Services.Implementation;
using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Renci.SshNet.Common;
using Renci.SshNet;
using Microsoft.AspNetCore.RateLimiting;

namespace JSAPNEW.Controllers
{

    [Route("api/files")]
    [ApiController]
    [Authorize]
    [EnableRateLimiting("FileTransfer")]
    public class FileController : ControllerBase
    {

        private readonly string _sftpHost;
        private readonly string _sftpUsername;
        private readonly string _sftpPassword;
        private readonly int _sftpPort;

        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly ILogger<FileController> _logger;
        public FileController(IWebHostEnvironment hostingEnvironment, IConfiguration configuration, ILogger<FileController> logger)
        {
            _hostingEnvironment = hostingEnvironment;
            _logger = logger;
            _sftpHost = Environment.GetEnvironmentVariable("JSAP_SFTP_HOST") ?? configuration["Sftp:Host"] ?? string.Empty;
            _sftpUsername = Environment.GetEnvironmentVariable("JSAP_SFTP_USERNAME") ?? configuration["Sftp:Username"] ?? string.Empty;
            _sftpPassword = Environment.GetEnvironmentVariable("JSAP_SFTP_PASSWORD") ?? configuration["Sftp:Password"] ?? string.Empty;
            var configuredPort = Environment.GetEnvironmentVariable("JSAP_SFTP_PORT") ?? configuration["Sftp:Port"];
            _sftpPort = int.TryParse(configuredPort, out var port) ? port : 22;
        }

        static string ConvertToCygwinPath(string windowsPath)
        {
            if (string.IsNullOrWhiteSpace(windowsPath))
                throw new ArgumentException("Path cannot be null or empty.");

            // Replace backslashes with forward slashes
            string unixPath = windowsPath.Replace("\\", "/");

            // Convert drive letter to Cygwin format
            if (unixPath.Length > 1 && unixPath[1] == ':')
            {
                string driveLetter = unixPath.Substring(0, 1).ToLower();
                unixPath = "/cygdrive/" + driveLetter + unixPath.Substring(2);
            }

            return unixPath;
        }

        [HttpGet("download")]
        public IActionResult DownloadFile(string filePath, string fileName, string fileExt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_sftpHost) ||
                    string.IsNullOrWhiteSpace(_sftpUsername) ||
                    string.IsNullOrWhiteSpace(_sftpPassword))
                {
                    _logger.LogError("SFTP credentials are not configured.");
                    return StatusCode(500, new { Success = false, Message = "Something went wrong" });
                }

                string remotePath = Uri.UnescapeDataString(filePath) + "/" + Uri.UnescapeDataString(fileName) + "." + Uri.UnescapeDataString(fileExt);
                remotePath = ConvertToCygwinPath(remotePath);
                string localTempPath = Path.Combine(Path.GetTempPath(), fileName + "." + fileExt);

                using (var sftp = new SftpClient(_sftpHost, _sftpPort, _sftpUsername, _sftpPassword))
                {
                    sftp.Connect();

                    if (!sftp.Exists(remotePath))
                    {
                        sftp.Disconnect();
                        return NotFound(new { Success = false, Message = "File not found on SFTP server" });
                    }

                    using (var fileStream = System.IO.File.Create(localTempPath))
                    {
                        sftp.DownloadFile(remotePath, fileStream);
                    }

                    sftp.Disconnect();
                }

                if (!System.IO.File.Exists(localTempPath))
                {
                    return NotFound(new { Success = false, Message = "File download failed or missing locally" });
                }

                var fileBytes = System.IO.File.ReadAllBytes(localTempPath);

                // **Special handling for OCX files**
                if (fileExt.ToLower() == "ocx")
                {
                    return File(fileBytes, "application/octet-stream", fileName + "." + fileExt);
                }

                // **For all other file types, use the correct MIME type**
                string mimeType = GetMimeType(fileExt);
                return File(fileBytes, mimeType, fileName + "." + fileExt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file from SFTP.");
                return StatusCode(500, new { Success = false, Message = "Something went wrong" });
            }
        }

        // **Function to get the correct MIME type**
        private string GetMimeType(string fileExt)
        {
            return fileExt.ToLower() switch
            {
                "bmp" => "image/bmp",
                "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "jfif" => "image/jpeg",
                "jpeg" => "image/jpeg",
                "jpg" => "image/jpeg",
                "pdf" => "application/pdf",
                "png" => "image/png",
                "xls" => "application/vnd.ms-excel",
                "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream" // Default fallback
            };
        }

        [HttpGet("advancedownload")]
        public IActionResult AdvanceDownloadFile([FromQuery] string filePath, [FromQuery] string fileName, [FromQuery] string fileExt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(fileExt))
                    return BadRequest("Invalid file details");

                // Normalize the file path and ensure it doesn't contain dangerous characters
                var normalizedPath = filePath.Replace("\\", "/").TrimStart('/');

                // Prevent directory traversal attacks
                if (normalizedPath.Contains("..") || normalizedPath.Contains("~"))
                    return BadRequest("Invalid file path");

                // Try multiple possible locations
                var possiblePaths = new List<string>
        {
            // Original path with wwwroot
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", normalizedPath, $"{fileName}.{fileExt}"),
            // Without wwwroot
            Path.Combine(Directory.GetCurrentDirectory(), normalizedPath, $"{fileName}.{fileExt}"),
            // With leading slash
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", filePath.TrimStart('/'), $"{fileName}.{fileExt}"),
            // Direct path
            Path.Combine(Directory.GetCurrentDirectory(), filePath.Replace("\\", "/"), $"{fileName}.{fileExt}")
        };

                string foundPath = null;
                foreach (var testPath in possiblePaths)
                {
                    var fullPath = Path.GetFullPath(testPath);

                    // Security check for wwwroot paths
                    var wwwrootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
                    var currentDirPath = Path.GetFullPath(Directory.GetCurrentDirectory());

                    if (fullPath.StartsWith(wwwrootPath) || fullPath.StartsWith(currentDirPath))
                    {
                        if (System.IO.File.Exists(fullPath))
                        {
                            foundPath = fullPath;
                            break;
                        }
                    }
                }

                if (foundPath == null)
                {
                    // Debug information - remove in production
                    var debugInfo = new
                    {
                        RequestedFile = $"{fileName}.{fileExt}",
                        RequestedPath = filePath,
                        NormalizedPath = normalizedPath,
                        SearchedPaths = possiblePaths.Select(p => Path.GetFullPath(p)).ToList(),
                        CurrentDirectory = Directory.GetCurrentDirectory(),
                        WwwrootExists = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")),
                        UploadsExists = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads"))
                    };

                    // Log this information or return it for debugging
                    // _logger.LogWarning("File not found. Debug info: {@DebugInfo}", debugInfo);

                    return NotFound($"File not found: {fileName}.{fileExt}. Debug: {System.Text.Json.JsonSerializer.Serialize(debugInfo)}");
                }

                var mimeType = GetMimeType(fileExt);
                var fileBytes = System.IO.File.ReadAllBytes(foundPath);
                return File(fileBytes, mimeType, $"{fileName}.{fileExt}");
            }
            catch (Exception ex)
            {
                // _logger.LogError(ex, "Error downloading file: {FileName}", fileName);
                return StatusCode(500, $"Error processing file download: {ex.Message}");
            }
        }

        // Helper method to check file locations
        [HttpGet("debug-file-location")]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult DebugFileLocation(string filePath, string fileName, string fileExt)
        {
            var wwwrootAdvancePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads", "Advancepayment");
            var rootAdvancePath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Advancepayment");

            var info = new
            {
                CurrentDirectory = Directory.GetCurrentDirectory(),
                RequestedFile = $"{fileName}.{fileExt}",
                RequestedPath = filePath,

                // Check directory existence
                DirectoryInfo = new
                {
                    WwwrootUploadsExists = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads")),
                    WwwrootAdvancepaymentExists = Directory.Exists(wwwrootAdvancePath),
                    RootAdvancepaymentExists = Directory.Exists(rootAdvancePath)
                },

                // List files in potential directories
                FilesInWwwrootAdvancepayment = Directory.Exists(wwwrootAdvancePath)
                    ? Directory.GetFiles(wwwrootAdvancePath).Select(Path.GetFileName).ToArray()
                    : new string[] { "Directory does not exist" },

                FilesInRootAdvancepayment = Directory.Exists(rootAdvancePath)
                    ? Directory.GetFiles(rootAdvancePath).Select(Path.GetFileName).ToArray()
                    : new string[] { "Directory does not exist" },

                // Check for similar filenames (in case of naming issues)
                SimilarFiles = Directory.Exists(wwwrootAdvancePath)
                    ? Directory.GetFiles(wwwrootAdvancePath, "*report*", SearchOption.TopDirectoryOnly)
                        .Select(f => Path.GetFileName(f)).ToArray()
                    : new string[0],

                // Check all Excel files
                ExcelFiles = Directory.Exists(wwwrootAdvancePath)
                    ? Directory.GetFiles(wwwrootAdvancePath, "*.xlsx", SearchOption.TopDirectoryOnly)
                        .Select(f => Path.GetFileName(f)).ToArray()
                    : new string[0]
            };

            return Ok(info);
        }

        // List all files in Uploads directory
        [HttpGet("debug-uploads-structure")]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult DebugUploadsStructure()
        {
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Uploads");

            if (!Directory.Exists(uploadsPath))
            {
                return Ok(new { Message = "Uploads directory does not exist", Path = uploadsPath });
            }

            var structure = GetDirectoryStructure(uploadsPath, 3); // Max 3 levels deep

            return Ok(new
            {
                UploadsPath = uploadsPath,
                Structure = structure
            });
        }

        private object GetDirectoryStructure(string path, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth >= maxDepth || !Directory.Exists(path))
                return null;

            try
            {
                return new
                {
                    Name = Path.GetFileName(path),
                    Path = path,
                    Files = Directory.GetFiles(path).Select(f => Path.GetFileName(f)).ToArray(),
                    Subdirectories = Directory.GetDirectories(path)
                        .Select(d => GetDirectoryStructure(d, maxDepth, currentDepth + 1))
                        .Where(d => d != null)
                        .ToArray()
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new { Name = Path.GetFileName(path), Error = "Access Denied" };
            }
        }
    }
}
