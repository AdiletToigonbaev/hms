/** Имена cookie сессии. Все httpOnly — токены недоступны JavaScript в браузере. */
export const ACCESS_COOKIE = "hms_at";
export const REFRESH_COOKIE = "hms_rt";
/** Признак «нужно сменить временный пароль» — для перенаправления в интерфейсе (API проверяет сам). */
export const MUST_CHANGE_COOKIE = "hms_mcp";

export interface CookieWrite {
  name: string;
  value: string;
  httpOnly: true;
  secure: boolean;
  sameSite: "lax";
  path: "/";
  expires: Date;
}

const secure = process.env.NODE_ENV === "production";

export function sessionCookies(tokens: {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: { mustChangePassword: boolean };
}): CookieWrite[] {
  const base = { httpOnly: true as const, secure, sameSite: "lax" as const, path: "/" as const };
  const list: CookieWrite[] = [
    { ...base, name: ACCESS_COOKIE, value: tokens.accessToken, expires: new Date(tokens.accessTokenExpiresAt) },
    { ...base, name: REFRESH_COOKIE, value: tokens.refreshToken, expires: new Date(tokens.refreshTokenExpiresAt) },
  ];
  list.push({
    ...base,
    name: MUST_CHANGE_COOKIE,
    value: tokens.user.mustChangePassword ? "1" : "",
    expires: tokens.user.mustChangePassword ? new Date(tokens.refreshTokenExpiresAt) : new Date(0),
  });
  return list;
}

export const allSessionCookies = [ACCESS_COOKIE, REFRESH_COOKIE, MUST_CHANGE_COOKIE] as const;
