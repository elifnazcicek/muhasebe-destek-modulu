import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../services/api.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit {
  mode: 'login' | 'register' = 'login';
  
  username = '';
  password = '';
  confirmPassword = '';
  email = ''; // E-posta alanı
  rememberMe = false;

  errorMessage = '';
  successMessage = '';
  loading = false;

  constructor(private router: Router, private apiService: ApiService, private cdr: ChangeDetectorRef) {
    if (localStorage.getItem('isLoggedIn') === 'true') {
      this.router.navigate(['/dashboard']);
    }
  }

  ngOnInit(): void {
    const savedRememberMe = localStorage.getItem('rememberMe') === 'true';
    if (savedRememberMe) {
      this.rememberMe = true;
      this.username = localStorage.getItem('rememberedUsername') || '';
      this.password = localStorage.getItem('rememberedPassword') || '';
    }
  }

  toggleMode(): void {
    this.mode = this.mode === 'login' ? 'register' : 'login';
    this.errorMessage = '';
    this.successMessage = '';
    this.password = '';
    this.confirmPassword = '';
    this.email = '';
    
    if (this.mode === 'register') {
      this.username = '';
    } else {
      const savedRememberMe = localStorage.getItem('rememberMe') === 'true';
      if (savedRememberMe) {
        this.rememberMe = true;
        this.username = localStorage.getItem('rememberedUsername') || '';
        this.password = localStorage.getItem('rememberedPassword') || '';
      }
    }
  }

  onSubmit(): void {
    if (this.mode === 'login') {
      this.handleLogin();
    } else {
      this.handleRegister();
    }
  }

  private handleLogin(): void {
    if (!this.username.trim() || !this.password.trim()) {
      this.errorMessage = 'Lütfen kullanıcı adı ve şifre giriniz.';
      return;
    }

    this.loading = true;
    this.errorMessage = '';

    this.apiService.login({ username: this.username, password: this.password }).subscribe({
      next: (res) => {
        console.log('Login next: Response received from API:', res);
        if (res.success) {
          localStorage.setItem('isLoggedIn', 'true');
          localStorage.setItem('username', res.username);
          localStorage.setItem('token', res.token);
          localStorage.setItem('role', res.role || 'User');

          if (this.rememberMe) {
            localStorage.setItem('rememberMe', 'true');
            localStorage.setItem('rememberedUsername', this.username);
            localStorage.setItem('rememberedPassword', this.password);
          } else {
            localStorage.removeItem('rememberMe');
            localStorage.removeItem('rememberedUsername');
            localStorage.removeItem('rememberedPassword');
          }

          this.loading = false;
          this.cdr.detectChanges();
          this.router.navigate(['/dashboard']);
        } else {
          this.loading = false;
          this.errorMessage = res.error || 'Giriş başarısız.';
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        console.log('Login error: Request failed:', err);
        this.loading = false;
        if (err.status === 401) {
          this.errorMessage = 'Hatalı kullanıcı adı veya şifre.';
        } else {
          this.errorMessage = err.error?.error || 'Sunucuyla bağlantı kurulamadı veya bir hata oluştu.';
        }
        this.cdr.detectChanges();
      }
    });
  }

  private handleRegister(): void {
    if (!this.username.trim() || !this.password.trim() || !this.confirmPassword.trim() || !this.email.trim()) {
      this.errorMessage = 'Lütfen tüm alanları doldurun.';
      return;
    }

    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(this.email.trim())) {
      this.errorMessage = 'Geçersiz e-posta adresi.';
      return;
    }

    if (this.password !== this.confirmPassword) {
      this.errorMessage = 'Şifreler eşleşmiyor.';
      return;
    }

    if (this.password.length < 3) {
      this.errorMessage = 'Şifre en az 3 karakter olmalıdır.';
      return;
    }

    this.loading = true;
    this.errorMessage = '';

    this.apiService.register({ username: this.username, password: this.password, email: this.email }).subscribe({
      next: (res) => {
        console.log('Register next: Response received from API:', res);
        if (res.success) {
          this.loading = false;
          this.successMessage = 'Profil başarıyla oluşturuldu! Giriş ekranına yönlendiriliyorsunuz...';
          this.cdr.detectChanges();

          setTimeout(() => {
            const tempUsername = this.username;
            const tempPassword = this.password;
            this.toggleMode();
            this.username = tempUsername;
            this.password = tempPassword;
            this.cdr.detectChanges();
          }, 2000);
        } else {
          this.loading = false;
          this.errorMessage = res.error || 'Kayıt başarısız.';
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        console.log('Register error: Request failed:', err);
        this.loading = false;
        this.errorMessage = err.error?.error || 'Kayıt sırasında bir sunucu hatası oluştu.';
        this.cdr.detectChanges();
      }
    });
  }
}
