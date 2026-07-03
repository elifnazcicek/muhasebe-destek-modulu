import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { ArchiveComponent } from './components/archive/archive.component';
import { LogsComponent } from './components/logs/logs.component';
import { UserManagementComponent } from './components/user-management/user-management.component';
import { DekontComponent } from './components/dekont/dekont.component';
import { HomeComponent } from './components/home/home.component';

export const routes: Routes = [
  { path: '', redirectTo: 'login', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'home', component: HomeComponent },
  { path: 'dashboard', component: DashboardComponent },
  { path: 'dekont', component: DekontComponent },
  { path: 'archive', component: ArchiveComponent },
  { path: 'logs', component: LogsComponent },
  { path: 'users', component: UserManagementComponent },
];
