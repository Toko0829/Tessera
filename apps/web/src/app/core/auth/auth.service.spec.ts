import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('stores the access token after a successful login', () => {
    expect(service.isAuthenticated()).toBe(false);

    service.login({ email: 'a@tessera.test', password: 'Str0ng!Passphrase' }).subscribe();

    const request = http.expectOne('/auth/login');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ accessToken: 'token-123', expiresAt: '2026-01-01T00:00:00Z' });

    expect(service.isAuthenticated()).toBe(true);
    expect(service.token()).toBe('token-123');
  });

  it('clears the session on logout', () => {
    service.login({ email: 'a@tessera.test', password: 'x' }).subscribe();
    http.expectOne('/auth/login').flush({ accessToken: 't', expiresAt: '' });
    expect(service.isAuthenticated()).toBe(true);

    service.logout().subscribe();
    http.expectOne('/auth/logout').flush(null);

    expect(service.isAuthenticated()).toBe(false);
  });

  it('restores the session from the refresh cookie', () => {
    service.restoreSession().subscribe();

    const request = http.expectOne('/auth/refresh');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ accessToken: 'restored', expiresAt: '' });

    expect(service.isAuthenticated()).toBe(true);
  });

  it('stays logged out when there is no valid refresh cookie', () => {
    service.restoreSession().subscribe();
    http.expectOne('/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(service.isAuthenticated()).toBe(false);
  });
});
