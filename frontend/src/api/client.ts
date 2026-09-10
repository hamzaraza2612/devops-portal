// Thin fetch wrapper. All requests go through here so token attachment,
// error normalization, and the "session expired" signal live in one place.

const TOKEN_STORAGE_KEY = 'devops-portal.token';

/** sessionStorage, not localStorage: the token is cleared when the tab
 * closes rather than persisting indefinitely on disk. Not a substitute for
 * an httpOnly cookie (still readable by any script on the page), but it
 * shrinks the exposure window — a documented, deliberate tradeoff (see
 * PROJECT_STATE.md Phase 4 "known limitations"; an httpOnly-cookie-based
 * session is the natural follow-up if XSS risk needs to be fully closed). */
export function getToken(): string | null {
  try {
    return sessionStorage.getItem(TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setToken(token: string | null): void {
  try {
    if (token) sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
    else sessionStorage.removeItem(TOKEN_STORAGE_KEY);
  } catch {
    // Storage unavailable (private browsing, disabled storage) — the app
    // still works for the current page load, just won't survive a refresh.
  }
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }

  get isUnauthorized() {
    return this.status === 401;
  }

  get isForbidden() {
    return this.status === 403;
  }

  get isNotFound() {
    return this.status === 404;
  }

  get isConflict() {
    return this.status === 409;
  }
}

/** Fired whenever a request comes back 401 — the token is missing/expired.
 * AuthContext listens for this to clear session state and redirect to
 * login, so any deep API call (not just ones made through AuthContext) can
 * trigger the same "you were logged out" handling. */
export const SESSION_EXPIRED_EVENT = 'devops-portal:session-expired';

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';

  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
      signal: options.signal,
    });
  } catch {
    throw new ApiError(0, 'Could not reach the server. Check your network connection and try again.');
  }

  if (response.status === 401) {
    window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
    throw new ApiError(401, 'Your session has expired. Please sign in again.');
  }

  if (response.status === 204) return undefined as T;

  const text = await response.text();
  const data = text ? safeJsonParse(text) : undefined;

  if (!response.ok) {
    const errorField = data && typeof data === 'object' && 'error' in data ? (data as { error: unknown }).error : undefined;
    const message = typeof errorField === 'string' && errorField ? errorField : `Request failed (HTTP ${response.status}).`;
    throw new ApiError(response.status, message);
  }

  return data as T;
}

function safeJsonParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: 'GET', signal }),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) => request<T>(path, { method: 'POST', body: body ?? {}, signal }),
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) => request<T>(path, { method: 'PUT', body: body ?? {}, signal }),
  del: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: 'DELETE', signal }),
};

/** Human-readable message for any thrown value — used at every call site so
 * a raw stack trace or [object Object] never reaches the screen. */
export function describeError(err: unknown): string {
  if (err instanceof ApiError) return err.message;
  if (err instanceof Error) return err.message;
  return 'Something went wrong. Please try again.';
}
