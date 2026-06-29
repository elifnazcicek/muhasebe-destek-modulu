using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReceiptOCR.API.Data;
using ReceiptOCR.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using ReceiptOCR.API.Services;

namespace ReceiptOCR.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ReceiptDbContext _context;
        private readonly IEmailService _emailService;

        public AuthController(IConfiguration configuration, ReceiptDbContext context, IEmailService emailService)
        {
            _configuration = configuration;
            _context = context;
            _emailService = emailService;
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
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new AuthResponse { Success = false, Error = "Kullanıcı adı, şifre ve e-posta zorunludur." });

            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
                return BadRequest(new AuthResponse { Success = false, Error = "Bu kullanıcı adı zaten alınmış." });

            if (await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower()))
                return BadRequest(new AuthResponse { Success = false, Error = "Bu e-posta adresi zaten kullanımda." });

            var user = new User
            {
                Username = request.Username,
                PasswordHash = HashPassword(request.Password),
                FullName = request.Username, // Varsayılan olarak username atanıyor
                Email = request.Email,
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

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username))
                return BadRequest(new { success = false, error = "Kullanıcı adı zorunludur." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (user == null)
            {
                return BadRequest(new { success = false, error = "Kullanıcı bulunamadı." });
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return BadRequest(new { success = false, error = "Kullanıcıya ait kayıtlı bir e-posta adresi bulunamadı. Lütfen yöneticinizle iletişime geçin." });
            }

            var random = new Random();
            var code = random.Next(100000, 999999).ToString();

            var activeResets = await _context.PasswordResets
                .Where(pr => pr.Username.ToLower() == request.Username.ToLower() && !pr.IsUsed)
                .ToListAsync();
            foreach (var r in activeResets)
            {
                r.IsUsed = true;
            }

            var passwordReset = new PasswordReset
            {
                Username = user.Username,
                Code = code,
                ExpiryTime = DateTime.Now.AddMinutes(10),
                IsUsed = false,
                CreatedDate = DateTime.Now
            };

            _context.PasswordResets.Add(passwordReset);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendVerificationCodeAsync(user.Email, code);

                _context.SystemLogs.Add(new SystemLog
                {
                    Username = user.Username,
                    ActionType = "FORGOT_PASSWORD",
                    Status = "SUCCESS",
                    Details = $"Şifre sıfırlama kodu gönderildi: {MaskEmail(user.Email)}"
                });
                await _context.SaveChangesAsync();

                return Ok(new { success = true, email = MaskEmail(user.Email), message = "Doğrulama kodu e-postanıza gönderildi." });
            }
            catch (Exception ex)
            {
                _context.ErrorLogs.Add(new ErrorLog
                {
                    Username = user.Username,
                    ActionType = "FORGOT_PASSWORD",
                    ErrorMessage = $"Şifre sıfırlama maili gönderme hatası: {ex.Message}",
                    StackTrace = ex.StackTrace,
                    Timestamp = DateTime.Now
                });
                await _context.SaveChangesAsync();

                return StatusCode(500, new { success = false, error = "Doğrulama kodu e-postası gönderilemedi: " + ex.Message });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest(new { success = false, error = "Tüm alanlar zorunludur." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (user == null)
                return BadRequest(new { success = false, error = "Kullanıcı bulunamadı." });

            var resetRecord = await _context.PasswordResets
                .FirstOrDefaultAsync(pr => pr.Username.ToLower() == request.Username.ToLower() && pr.Code == request.Code && !pr.IsUsed);

            if (resetRecord == null)
                return BadRequest(new { success = false, error = "Geçersiz doğrulama kodu." });

            if (resetRecord.ExpiryTime < DateTime.Now)
                return BadRequest(new { success = false, error = "Doğrulama kodunun süresi dolmuş (10 dakika)." });

            user.PasswordHash = HashPassword(request.NewPassword);
            resetRecord.IsUsed = true;

            _context.SystemLogs.Add(new SystemLog
            {
                Username = user.Username,
                ActionType = "RESET_PASSWORD",
                Status = "SUCCESS",
                Details = "Şifre başarıyla sıfırlandı."
            });

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Şifreniz başarıyla sıfırlandı. Yeni şifrenizle giriş yapabilirsiniz." });
        }

        private string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                return email;

            var parts = email.Split('@');
            var name = parts[0];
            var domain = parts[1];

            if (name.Length <= 2)
                return name[0] + "***@" + domain;

            return name.Substring(0, 2) + new string('*', name.Length - 2) + "@" + domain;
        }
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
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

    public class ForgotPasswordRequest
    {
        public string Username { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
