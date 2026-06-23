import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-archive',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './archive.component.html',
  styleUrls: ['./archive.component.css']
})
export class ArchiveComponent implements OnInit {
  receipts: any[] = [];
  filteredReceipts: any[] = [];
  searchQuery: string = '';
  loading: boolean = false;
  errorMessage: string = '';

  constructor(private apiService: ApiService, private router: Router) {}

  ngOnInit(): void {
    this.fetchReceipts();
  }

  fetchReceipts(): void {
    this.loading = true;
    this.errorMessage = '';
    this.apiService.getReceipts().subscribe({
      next: (data) => {
        this.receipts = data;
        this.filterReceipts();
        this.loading = false;
      },
      error: (err) => {
        this.errorMessage = 'Veriler yüklenirken hata oluştu: ' + err.message;
        this.loading = false;
      }
    });
  }

  filterReceipts(): void {
    if (!this.searchQuery.trim()) {
      this.filteredReceipts = this.receipts;
      return;
    }

    const query = this.searchQuery.toLowerCase();
    this.filteredReceipts = this.receipts.filter(r => 
      r.merchantName.toLowerCase().includes(query) ||
      r.receiptDate.toLowerCase().includes(query) ||
      r.id.toString().includes(query)
    );
  }

  editReceipt(id: number): void {
    this.router.navigate(['/dashboard'], { queryParams: { edit: id } });
  }

  downloadExcel(): void {
    window.open('http://localhost:5000/api/receipts/export', '_blank');
  }
}
