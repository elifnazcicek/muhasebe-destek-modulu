import { Component, signal } from '@angular/core';
import { RouterOutlet, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('frontend');

  constructor(private router: Router) {}

  isLoggedIn(): boolean {
    return localStorage.getItem('isLoggedIn') === 'true';
  }

  getUsername(): string {
    return localStorage.getItem('username') || '';
  }

  isAdmin(): boolean {
    return localStorage.getItem('role') === 'Admin';
  }

  logout(): void {
    localStorage.removeItem('isLoggedIn');
    localStorage.removeItem('username');
    localStorage.removeItem('token');
    this.router.navigate(['/login']);
  }

  downloadExcel(): void {
    const username = localStorage.getItem('username') || 'default';
    // Arkadaşın backend'inde henüz excel export olmadığı için geçici olarak yerel backend portuna yönlendiriyoruz
    window.open(`http://localhost:5000/api/receipts/export?username=${encodeURIComponent(username)}`, '_blank');
  }
}
