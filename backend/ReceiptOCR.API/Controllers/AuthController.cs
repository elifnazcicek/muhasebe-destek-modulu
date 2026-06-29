using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ReceiptOCR.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ReceiptDbContext _context;

        public AuthController(IConfiguration configuration, ReceiptDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new AuthResponse { Success = false, Error = "Kullanıcı adı ve şifre zorunludur." });

            var passwordHash = HashPassword(request.Password);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower() && u.PasswordHash == passwordHash);

            if (user == null || !user.IsActive)
            {
                // Başarısız giriş denemesini logla
                _context.SystemLogs.Add(new SystemLog
                {
                    Username = request.Username,
                    ActionType = "LOGIN",
                    Status = "FAILED",
                    Details = "Başarısız giriş denemesi: Geçersiz kullanıcı adı veya şifre."
                });
                await _context.SaveChangesAsync();

                return Unauthorized(new AuthResponse { Success = false, Error = "Geçersiz kullanıcı adı veya şifre" });
            }

            var token = GenerateJwtToken(user);

            // Başarılı girişi logla
            _context.SystemLogs.Add(new SystemLog
            {
                Username = user.Username,
                ActionType = "LOGIN",
                Status = "SUCCESS",
                Details = "Kullanıcı başarıyla giriş yaptı."
            });
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                Success = true,
                Token = token,
                Username = user.Username,
                Role = user.Role
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new AuthResponse { Success = false, Error = "Kullanıcı adı ve şifre zorunludur." });

            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
                return BadRequest(new AuthResponse { Success = false, Error = "Bu kullanıcı adı zaten alınmış." });

            var user = new User
            {
                Username = request.Username,
                PasswordHash = HashPassword(request.Password),
                FullName = request.Username, // Varsayılan olarak username atanıyor
                Role = "User",
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _context.Users.Add(user);

            // Başarılı kayıt işlemini logla
            _context.SystemLogs.Add(new SystemLog
            {
                Username = user.Username,
                ActionType = "REGISTER",
                Status = "SUCCESS",
                Details = "Yeni kullanıcı hesabı oluşturuldu."
            });

            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                Success = true,
                Username = user.Username,
                Token = GenerateJwtToken(user),
                Role = user.Role
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                _context.SystemLogs.Add(new SystemLog
                {
                    Username = request.Username,
                    ActionType = "LOGOUT",
                    Status = "SUCCESS",
                    Details = "Kullanıcı sistemden çıkış yaptı."
                });
                await _context.SaveChangesAsync();
            }
            return Ok(new { success = true });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers([FromQuery] string adminUsername)
        {
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == adminUsername.ToLower());
            if (adminUser == null || adminUser.Role != "Admin")
            {
                return StatusCode(403, new { error = "Kullanıcı listesini görüntülemek için yönetici yetkiniz olmalıdır." });
            }

            var users = await _context.Users
                .OrderBy(u => u.Username)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.FullName,
                    u.Role,
                    u.IsActive,
                    CreatedDate = u.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] UpdateRoleRequest request)
        {
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.AdminUsername.ToLower());
            if (adminUser == null || adminUser.Role != "Admin")
            {
                return StatusCode(403, new { error = "Kullanıcı rolünü değiştirmek için yönetici yetkiniz olmalıdır." });
            }

            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null)
            {
                return NotFound(new { error = "Kullanıcı bulunamadı." });
            }

            if (request.Role != "Admin" && request.Role != "User")
            {
                return BadRequest(new { error = "Geçersiz rol tanımı." });
            }

            string oldRole = userToUpdate.Role;
            userToUpdate.Role = request.Role;

            _context.SystemLogs.Add(new SystemLog
            {
                Username = request.AdminUsername,
                ActionType = "UPDATE_USER_ROLE",
                Status = "SUCCESS",
                Details = $"Kullanıcı rolü güncellendi: {userToUpdate.Username} ({oldRole} -> {request.Role})"
            });

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [HttpPut("users/{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
        {
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.AdminUsername.ToLower());
            if (adminUser == null || adminUser.Role != "Admin")
            {
                return StatusCode(403, new { error = "Kullanıcı durumunu değiştirmek için yönetici yetkiniz olmalıdır." });
            }

            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null)
            {
                return NotFound(new { error = "Kullanıcı bulunamadı." });
            }

            if (userToUpdate.Username.ToLower() == request.AdminUsername.ToLower())
            {
                return BadRequest(new { error = "Kendi yöneticilik hesabınızı donduramazsınız." });
            }

            userToUpdate.IsActive = request.IsActive;

            _context.SystemLogs.Add(new SystemLog
            {
                Username = request.AdminUsername,
                ActionType = "UPDATE_USER_STATUS",
                Status = "SUCCESS",
                Details = $"Kullanıcı aktiflik durumu güncellendi: {userToUpdate.Username} (Aktif: {request.IsActive})"
            });

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var builder = new StringBuilder();
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtKey = _configuration["Jwt:Key"] ?? "super_secret_key_for_receipt_ocr_app_1234567890123456";
            var key = Encoding.ASCII.GetBytes(jwtKey);
            
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                }),
                Expires = DateTime.UtcNow.AddDays(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _configuration["Jwt:Issuer"] ?? "ReceiptOCR",
                Audience = _configuration["Jwt:Audience"] ?? "ReceiptOCR"
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LogoutRequest
    {
        public string Username { get; set; } = string.Empty;
    }

    public class UpdateRoleRequest
    {
        public string AdminUsername { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class UpdateStatusRequest
    {
        public string AdminUsername { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
