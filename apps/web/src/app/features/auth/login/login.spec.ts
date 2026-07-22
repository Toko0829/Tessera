import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function fillForm(email: string, password: string): void {
    const root = fixture.nativeElement as HTMLElement;
    const emailInput = root.querySelector<HTMLInputElement>('#email')!;
    const passwordInput = root.querySelector<HTMLInputElement>('#password')!;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submitForm(): void {
    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  it('logs in and navigates home on success', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    fillForm('a@tessera.test', 'Str0ng!Passphrase');
    submitForm();

    http.expectOne('/auth/login').flush({ accessToken: 't', expiresAt: '' });

    expect(navigate).toHaveBeenCalledWith('/home');
  });

  it('shows an error when login fails', () => {
    fillForm('a@tessera.test', 'wrongpass');
    submitForm();

    http.expectOne('/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role=alert]');
    expect(alert?.textContent).toContain('not right');
  });
});
