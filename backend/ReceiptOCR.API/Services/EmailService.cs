using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ReceiptOCR.API.Services
{
    public interface IEmailService
    {
        Task SendVerificationCodeAsync(string toEmail, string code);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendVerificationCodeAsync(string toEmail, string code)
        {
            try
            {
                var smtpSection = _configuration.GetSection("Smtp");
                var host = smtpSection["Host"] ?? "smtp.gmail.com";
                var port = int.Parse(smtpSection["Port"] ?? "587");
                var username = smtpSection["Username"] ?? "";
                var password = smtpSection["Password"] ?? "";
                var enableSsl = bool.Parse(smtpSection["EnableSsl"] ?? "true");
                var senderEmail = smtpSection["SenderEmail"] ?? "";
                var senderName = smtpSection["SenderName"] ?? "Fis Okuma Otomasyonu";

                // Simülasyon Modu Kontrolü:
                // Şifre varsayılan değerdeyse veya boşsa, gerçek mail atmak yerine yerel loga ve dosyaya yazar.
                if (password == "ornek_sifre_buraya_yazilacak" || string.IsNullOrWhiteSpace(password) || username.Contains("muhasebe_kod_dogrulama"))
                {
                    _logger.LogWarning("==================================================");
                    _logger.LogWarning("SMTP E-POSTA AYARLARI YAPILANDIRILMAMIŞ (SIMULASYON AKTIF)");
                    _logger.LogWarning($"ALICI: {toEmail}");
                    _logger.LogWarning($"DOĞRULAMA KODU: {code}");
                    _logger.LogWarning("==================================================");

                    // Hem uygulama dizinindeki logs hem de proje kök dizinindeki logs klasörlerine yaz
                    string[] pathsToTry = {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "emails"),
                        "C:\\Projeler\\receipt_reg_auto\\receipt_reg_auto\\logs\\emails"
                    };

                    foreach (var dir in pathsToTry)
                    {
                        try
                        {
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            var filePath = Path.Combine(dir, $"{toEmail.Replace("@", "_")}_code.txt");
                            await File.WriteAllTextAsync(filePath, $"Dogrulama Kodu: {code}\nTarih: {DateTime.Now}\nAlici: {toEmail}");
                        }
                        catch {}
                    }

                    return;
                }

                _logger.LogInformation("E-posta gönderiliyor: Alici={toEmail}, Gonderen={senderEmail}", toEmail, senderEmail);

                using (var client = new SmtpClient(host, port))
                {
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(username, password);
                    client.EnableSsl = enableSsl;

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(senderEmail, senderName),
                        Subject = "Sifre Sifirlama Dogrulama Kodu",
                        Body = $@"
                        <h3>Sifre Sifirlama Talebi</h3>
                        <p>SmartReceipt Fis Okuma ve Otomasyonu sisteminde sifrenizi sifirlamak icin bir talepte bulundunuz.</p>
                        <p>Sifre sifirlama dogrulama kodunuz:</p>
                        <h2 style='color: #10b981; font-size: 24px; letter-spacing: 2px;'>{code}</h2>
                        <p>Bu kod <strong>10 dakika</strong> gecerlidir. Eger bu talebi siz yapmadiysaniz lutfen bu e-postayi dikkate almayiniz.</p>
                        <hr/>
                        <p style='font-size: 11px; color: #888;'>Bu mail otomatik olarak gonderilmistir. Lutfen cevaplamayiniz.</p>",
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                    _logger.LogInformation("E-posta basariyla gonderildi: Alici={toEmail}", toEmail);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E-posta gonderme hatasi! Alici={toEmail}", toEmail);
                throw new Exception("E-posta gonderimi basarisiz oldu: " + ex.Message, ex);
            }
        }
    }
}
