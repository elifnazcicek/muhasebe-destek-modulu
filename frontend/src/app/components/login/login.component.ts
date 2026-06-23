import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

interface UserProfile {
  username: string;
  email?: string;
  password?: string;
}

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
  rememberMe = false;

  errorMessage = '';
  successMessage = '';
  loading = false;

  constructor(private router: Router) {
    // If already logged in, redirect straight to dashboard
    if (localStorage.getItem('isLoggedIn') === 'true') {
      this.router.navigate(['/dashboard']);
    }
  }

  ngOnInit(): void {
    // Initialize default users in localStorage if empty
    let users = JSON.parse(localStorage.getItem('users') || '[]');
    if (users.length === 0) {
      users.push({ username: 'admin', password: '123' });
      localStorage.setItem('users', JSON.stringify(users));
    }

    // Check if "Remember Me" credentials exist
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
    
    if (this.mode === 'register') {
      this.username = '';
    } else {
      // Restore remembered credentials if switching back to login
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

    setTimeout(() => {
      const users: UserProfile[] = JSON.parse(localStorage.getItem('users') || '[]');
      const matchedUser = users.find(
        (u) => u.username.toLowerCase() === this.username.toLowerCase() && u.password === this.password
      );

      if (matchedUser) {
        // Save login state
        localStorage.setItem('isLoggedIn', 'true');
        localStorage.setItem('username', matchedUser.username);

        // Save or clear Remember Me credentials
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
        this.router.navigate(['/dashboard']);
      } else {
        this.loading = false;
        this.errorMessage = 'Hatalı kullanıcı adı veya şifre.';
      }
    }, 800);
  }

  private handleRegister(): void {
    if (!this.username.trim() || !this.password.trim() || !this.confirmPassword.trim()) {
      this.errorMessage = 'Lütfen tüm alanları doldurun.';
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

    setTimeout(() => {
      let users: UserProfile[] = JSON.parse(localStorage.getItem('users') || '[]');
      const usernameExists = users.some(
        (u) => u.username.toLowerCase() === this.username.toLowerCase()
      );

      if (usernameExists) {
        this.loading = false;
        this.errorMessage = 'Bu kullanıcı adı zaten alınmış.';
        return;
      }

      // Add new profile
      users.push({
        username: this.username,
        password: this.password
      });
      localStorage.setItem('users', JSON.stringify(users));

      this.loading = false;
      this.successMessage = 'Profil başarıyla oluşturuldu! Giriş ekranına yönlendiriliyorsunuz...';

      setTimeout(() => {
        const tempUsername = this.username;
        const tempPassword = this.password;
        this.toggleMode();
        // Auto-populate for convenience after registration
        this.username = tempUsername;
        this.password = tempPassword;
      }, 2000);
    }, 800);
  }
}
