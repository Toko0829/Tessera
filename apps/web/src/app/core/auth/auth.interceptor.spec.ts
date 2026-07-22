import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    httpClient = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  function signIn(): void {
    auth.login({ email: 'a@tessera.test', password: 'x' }).subscribe();
    http.expectOne('/auth/login').flush({ accessToken: 'token-abc', expiresAt: '' });
  }

  it('attaches the token to our own API', () => {
    signIn();
    httpClient.get('/videos').subscribe();

    const request = http.expectOne('/videos');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-abc');
    request.flush([]);
  });

  it('never sends the token to an absolute storage URL', () => {
    signIn();
    httpClient.post('http://storage.local/bucket', new FormData()).subscribe();

    const request = http.expectOne('http://storage.local/bucket');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(null);
  });
});
