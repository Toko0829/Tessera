import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { Credentials, LoginResponse, MeResponse, RegisterResponse } from './auth.models';

// Auth state is a signal (the access token, in memory only). The HTTP calls are RxJS
// streams. The token never touches localStorage; the refresh token is an HttpOnly
// cookie the browser manages, so `withCredentials` is set on the calls that use it
// (CLAUDE.md section 6).
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly accessToken = signal<string | null>(null);

  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  token(): string | null {
    return this.accessToken();
  }

  register(credentials: Credentials): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>('/auth/register', credentials, { withCredentials: true });
  }

  login(credentials: Credentials): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>('/auth/login', credentials, { withCredentials: true })
      .pipe(tap((response) => this.accessToken.set(response.accessToken)));
  }

  // Exchanges the refresh cookie for a new access token (rotation happens server-side).
  refresh(): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>('/auth/refresh', {}, { withCredentials: true })
      .pipe(tap((response) => this.accessToken.set(response.accessToken)));
  }

  // Called once on app startup: restore the session from the refresh cookie if there
  // is one, otherwise stay logged out. Never throws, so bootstrap is not blocked.
  restoreSession(): Observable<void> {
    return this.refresh().pipe(
      map(() => undefined),
      catchError(() => of(undefined)),
    );
  }

  me(): Observable<MeResponse> {
    return this.http.get<MeResponse>('/auth/me');
  }

  logout(): Observable<void> {
    // Drop the local session immediately; the request revokes the refresh token
    // server-side. Local sign-out holds even if that call fails.
    this.accessToken.set(null);
    return this.http.post<void>('/auth/logout', {}, { withCredentials: true });
  }
}
