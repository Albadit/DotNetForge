/**
 * Same-origin API client for the admin SPA. All calls hit the cookie-authenticated `/admin-api`
 * surface on the ASP.NET Core host (NOT the token-only `/api`). On 401 we send the user to the
 * server-rendered login page; write requests carry the antiforgery token in an X-CSRF-TOKEN header.
 */
const BASE = "/admin-api";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

let csrfToken: string | null = null;

function redirectToLogin(): void {
  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
  window.location.href = `/account/login?returnUrl=${returnUrl}`;
}

async function ensureCsrf(): Promise<string> {
  if (csrfToken) return csrfToken;
  const res = await fetch(`${BASE}/antiforgery`, { credentials: "include" });
  if (res.status === 401) {
    redirectToLogin();
    throw new ApiError("Unauthorized", 401);
  }
  const data = (await res.json()) as { token: string };
  csrfToken = data.token;
  return csrfToken;
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    credentials: "include",
    headers: { Accept: "application/json" },
  });

  if (res.status === 401) {
    redirectToLogin();
    throw new ApiError("Unauthorized", 401);
  }
  if (!res.ok) {
    throw new ApiError(`Request failed (${res.status})`, res.status);
  }
  return (await res.json()) as T;
}

async function send<T>(method: "POST" | "PUT" | "DELETE", path: string, body?: unknown): Promise<T> {
  const token = await ensureCsrf();
  const res = await fetch(`${BASE}${path}`, {
    method,
    credentials: "include",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (res.status === 401) {
    redirectToLogin();
    throw new ApiError("Unauthorized", 401);
  }
  if (!res.ok) {
    throw new ApiError(`Request failed (${res.status})`, res.status);
  }
  return (res.status === 204 ? (undefined as T) : ((await res.json()) as T));
}

export const apiPost = <T>(path: string, body?: unknown) => send<T>("POST", path, body);
export const apiPut = <T>(path: string, body?: unknown) => send<T>("PUT", path, body);
export const apiDelete = <T>(path: string) => send<T>("DELETE", path);
