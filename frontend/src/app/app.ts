import { Component, signal } from '@angular/core';
import { RouterOutlet, Router } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('frontend');

  constructor(private router: Router) {}

  isLoggedIn(): boolean {
    return localStorage.getItem('isLoggedIn') === 'true';
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
