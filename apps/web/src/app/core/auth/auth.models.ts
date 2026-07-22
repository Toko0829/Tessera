export interface Credentials {
  readonly email: string;
  readonly password: string;
}

export interface LoginResponse {
  readonly accessToken: string;
  readonly expiresAt: string;
}

export interface RegisterResponse {
  readonly id: string;
}

export interface MeResponse {
  readonly id: string;
  readonly email: string;
}
