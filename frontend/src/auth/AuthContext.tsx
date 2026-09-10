import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { AuthApi } from '../api/endpoints';
import { SESSION_EXPIRED_EVENT, getToken, setToken } from '../api/client';
import type { UserDto } from '../types/api';

/**
 * Client-side permission checks here (and everywhere `can()` is used across
 * the app) decide what the UI *offers* — they are a convenience, not a
 * security boundary. Every mutating endpoint re-validates the caller's
 * permissions server-side (see DeploymentService/ExceptionHandlingMiddleware
 * in the backend), so a user who bypasses a hidden button still gets a 403
 * from the API. Never add a capability here that the backend doesn't also
 * enforce.
 */
interface AuthContextValue {
  user: UserDto | null;
  isLoading: boolean;
  error: string | null;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
  can: (permission: string) => boolean;
}

// Exported (not just the hook) so tests can supply a fixed value via
// `<AuthContext.Provider value={...}>` without exercising the real
// login/session-restore flow.
export const AuthContext = createContext<AuthContextValue | undefined>(undefined);
export type { AuthContextValue };

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const logout = useCallback(() => {
    setToken(null);
    setUser(null);
  }, []);

  useEffect(() => {
    let cancelled = false;
    async function restoreSession() {
      if (!getToken()) {
        setIsLoading(false);
        return;
      }
      try {
        const me = await AuthApi.me();
        if (!cancelled) setUser(me);
      } catch {
        if (!cancelled) setToken(null);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    }
    void restoreSession();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const handler = () => logout();
    window.addEventListener(SESSION_EXPIRED_EVENT, handler);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler);
  }, [logout]);

  const login = useCallback(async (username: string, password: string) => {
    setError(null);
    try {
      const result = await AuthApi.login({ username, password });
      setToken(result.token);
      setUser(result.user);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed.');
      throw err;
    }
  }, []);

  const can = useCallback((permission: string) => user?.permissions.includes(permission) ?? false, [user]);

  const value = useMemo<AuthContextValue>(
    () => ({ user, isLoading, error, login, logout, can }),
    [user, isLoading, error, login, logout, can],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider.');
  return ctx;
}
