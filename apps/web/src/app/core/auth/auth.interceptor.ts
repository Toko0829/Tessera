import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

// Attaches the in-memory access token to our own API requests (relative URLs). It is
// never sent to an absolute URL such as the object storage upload endpoint.
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthService).token();
  if (token && !/^https?:\/\//i.test(request.url)) {
    request = request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(request);
};
