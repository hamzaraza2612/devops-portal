import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { EnvironmentTiers, Permissions, hasEnvironmentAccess } from '../auth/permissions';

const navItems = [
  { to: '/', label: 'Dashboard', end: true, permission: null as string | null },
  { to: '/applications', label: 'Applications', permission: Permissions.ApplicationsView },
  { to: '/environments', label: 'Environments', permission: Permissions.EnvironmentsView },
  { to: '/pending', label: 'Pending Requests', permission: Permissions.DeploymentsView },
  { to: '/deployments', label: 'Deployment History', permission: Permissions.DeploymentsView },
  { to: '/credentials', label: 'Credentials', permission: Permissions.SecretsView },
];

const adminNavItems = [
  { to: '/admin/tenants', label: 'Tenants', permission: Permissions.TenantsView },
  { to: '/admin/users', label: 'Users', permission: Permissions.UsersView },
  { to: '/admin/roles', label: 'Roles', permission: Permissions.RolesView },
  { to: '/admin/repositories', label: 'Repositories', permission: Permissions.RepositoriesView },
  { to: '/admin/target-servers', label: 'Deployment Targets', permission: Permissions.TargetServersView },
  { to: '/admin/integrations', label: 'Integrations', permission: Permissions.BuildServersView },
];

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
    isActive ? 'bg-blue-600 text-white shadow-sm' : 'text-slate-300 hover:bg-white/5 hover:text-white'
  }`;

export function Layout() {
  const { user, logout, can } = useAuth();
  const [isNavOpen, setIsNavOpen] = useState(false);

  const visibleItems = navItems.filter((item) => !item.permission || can(item.permission));
  const visibleAdminItems = adminNavItems.filter((item) => can(item.permission));
  const visibleTiers = EnvironmentTiers.filter((tier) => hasEnvironmentAccess(user?.permissions ?? [], tier));

  const sidebarContent = (
    <div className="flex h-full flex-col">
      <div className="flex items-center gap-2.5 px-5 py-5">
        <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-blue-600 text-sm font-bold text-white">DP</span>
        <span className="text-base font-semibold tracking-tight text-white">DevOps Portal</span>
      </div>

      <nav className="flex-1 space-y-6 overflow-y-auto px-3 pb-6">
        <div className="space-y-0.5">
          {visibleItems.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end} onClick={() => setIsNavOpen(false)} className={navLinkClass}>
              {item.label}
            </NavLink>
          ))}
        </div>

        {visibleTiers.length > 0 && (
          <div>
            <p className="px-3 pb-1.5 text-[11px] font-semibold uppercase tracking-wider text-slate-500">Environments</p>
            <div className="space-y-0.5">
              {visibleTiers.map((tier) => (
                <NavLink key={tier} to={`/environments/${tier.toLowerCase()}`} onClick={() => setIsNavOpen(false)} className={navLinkClass}>
                  <span className={`h-1.5 w-1.5 rounded-full ${tierDotColor[tier]}`} />
                  {tier}
                </NavLink>
              ))}
            </div>
          </div>
        )}

        {visibleAdminItems.length > 0 && (
          <div>
            <p className="px-3 pb-1.5 text-[11px] font-semibold uppercase tracking-wider text-slate-500">Administration</p>
            <div className="space-y-0.5">
              {visibleAdminItems.map((item) => (
                <NavLink key={item.to} to={item.to} onClick={() => setIsNavOpen(false)} className={navLinkClass}>
                  {item.label}
                </NavLink>
              ))}
            </div>
          </div>
        )}
      </nav>

      {user && (
        <div className="border-t border-white/10 px-4 py-4">
          <p className="truncate text-sm font-medium text-white">{user.fullName}</p>
          <p className="truncate text-xs text-slate-400">{user.roles.join(', ') || 'No role'}</p>
          <button
            type="button"
            onClick={logout}
            className="mt-3 w-full rounded-md border border-white/15 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-white/5"
          >
            Sign out
          </button>
        </div>
      )}
    </div>
  );

  return (
    <div className="flex min-h-full">
      {/* Desktop sidebar */}
      <aside className="hidden w-64 shrink-0 bg-slate-900 md:block">{sidebarContent}</aside>

      {/* Mobile slide-over sidebar */}
      {isNavOpen && (
        <div className="fixed inset-0 z-40 md:hidden">
          <button type="button" aria-label="Close navigation" className="absolute inset-0 bg-black/40" onClick={() => setIsNavOpen(false)} />
          <aside className="absolute inset-y-0 left-0 w-64 bg-slate-900 shadow-xl">{sidebarContent}</aside>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center gap-3 border-b border-slate-200 bg-white px-4 py-3 md:hidden">
          <button
            type="button"
            className="rounded-md border border-slate-300 p-1.5 text-slate-600"
            onClick={() => setIsNavOpen((v) => !v)}
            aria-label="Toggle navigation"
          >
            ☰
          </button>
          <span className="text-base font-semibold text-slate-900">DevOps Portal</span>
        </header>
        <main className="flex-1 px-4 py-6 md:px-8">
          <div className="mx-auto max-w-7xl">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}

const tierDotColor: Record<(typeof EnvironmentTiers)[number], string> = {
  DEV: 'bg-slate-400',
  QA: 'bg-amber-400',
  UAT: 'bg-purple-400',
  PRODUCTION: 'bg-emerald-400',
};
