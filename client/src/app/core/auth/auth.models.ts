export interface UserInfo {
  id: number;
  email: string;
  fullName: string;
  academyId: number | null;
  roles: string[];
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  user: UserInfo;
}
