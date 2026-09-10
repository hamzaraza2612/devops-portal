import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';

const navItems = [
  { to: '/', label: 'Dashboard', end: true, permission: null as string | null },
  { to: '/applications', label: 'Applications', permission: Permissions.ApplicationsView },
  { to: '/environments', label: 'Environments', permission: Permissions.EnvironmentsView },
  { to: '/pending', label: 'Pending Requests', permission: Permissions.DeploymentsView },
  { to: '/deployments', label: 'Deployment History', permission: Permissions.DeploymentsView },
];

export function Layout() {
  const { user, logout, can } = useAuth();
  const [isNavOpen, setIsNavOpen] = useState(false);

  const visibleItems = navItems.filter((item) => !item.permission || can(item.permission));

  return (
    <div className="min-h-full">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-3">
          <div className="flex items-center gap-3">
            <button
              type="button"
              className="rounded-md border border-slate-300 p-1.5 text-slate-600 md:hidden"
              onClick={() => setIsNavOpen((v) => !v)}
              aria-label="Toggle navigation"
            >
              ☰
            </button>
            <span className="text-base font-semibold text-slate-900">DevOps Portal</span>
          </div>
          {user && (
            <div className="flex items-center gap-3 text-sm text-slate-600">
              <span className="hidden sm:inline">
                {user.fullName} <span className="text-slate-400">({user.roles.join(', ') || 'no role'})</span>
              </span>
              <button type="button" onClick={logout} className="rounded-md border border-slate-300 px-2.5 py-1 text-sm hover:bg-slate-50">
                Sign out
              </button>
            </div>
          )}
        </div>
        <nav className={`${isNavOpen ? 'block' : 'hidden'} border-t border-slate-200 md:block`}>
          <div className="mx-auto flex max-w-7xl flex-col gap-0.5 px-2 py-2 md:flex-row md:gap-4 md:px-4">
            {visibleItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                onClick={() => setIsNavOpen(false)}
                className={({ isActive }) =>
                  `rounded-md px-3 py-1.5 text-sm font-medium ${
                    isActive ? 'bg-slate-900 text-white' : 'text-slate-600 hover:bg-slate-100'
                  }`
                }
              >
                {item.label}
              </NavLink>
            ))}
          </div>
        </nav>
      </header>
      <main className="mx-auto max-w-7xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
