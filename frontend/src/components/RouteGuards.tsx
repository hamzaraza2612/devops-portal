import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { LoadingSpinner } from './Common';

export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) return <LoadingSpinner label="Checking session…" />;
  if (!user) return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  return <>{children}</>;
}

/** Extra defense for direct navigation to a route the nav already hides —
 * the API is still the real authority (every read endpoint behind this is
 * itself permission-checked; this just avoids rendering a page that would
 * only show 403 errors). */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const { can } = useAuth();
  if (!can(permission)) {
    return (
      <div className="rounded-lg border border-amber-200 bg-amber-50 p-6 text-center">
        <p className="text-sm font-medium text-amber-800">You don't have permission to view this page.</p>
        <p className="mt-1 text-sm text-amber-700">Contact your administrator if you believe this is a mistake.</p>
      </div>
    );
  }
  return <>{children}</>;
}
