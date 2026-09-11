import { useMemo, useState } from 'react';
import { RolesApi, UsersApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import type { RoleDto, UserDto } from '../../types/api';
import { formatRelative } from '../../utils/format';

async function loadUsers() {
  const [users, roles] = await Promise.all([UsersApi.list(), RolesApi.list()]);
  return { users, roles };
}

/** Tenant-scoped: only the current tenant's own users are ever visible or
 * creatable here — enforced server-side by AppDbContext's query filters, not
 * by anything in this page. */
export function UsersPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadUsers, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);

  if (isLoading) return <LoadingSpinner label="Loading users…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Users"
        subtitle="Users within your organization and the roles they hold."
        actions={
          can(Permissions.UsersManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New User'}
            </button>
          )
        }
      />

      {showCreate && (
        <CreateUserForm
          roles={data.roles}
          onCreated={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.users.length === 0 ? (
        <EmptyState title="No users yet." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Username</th>
                <th className="px-4 py-2">Email</th>
                <th className="px-4 py-2">Roles</th>
                <th className="px-4 py-2">Last login</th>
                <th className="px-4 py-2">Status</th>
                {can(Permissions.UsersManage) && <th className="px-4 py-2" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.users.map((user) => (
                <UserRow key={user.id} user={user} roles={data.roles} canManage={can(Permissions.UsersManage)} onChanged={reload} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function UserRow({ user, roles, canManage, onChanged }: { user: UserDto; roles: RoleDto[]; canManage: boolean; onChanged: () => void }) {
  const roleIds = useMemo(() => roles.filter((r) => user.roles.includes(r.name)).map((r) => r.id), [roles, user.roles]);

  return (
    <tr className={user.isActive ? '' : 'opacity-60'}>
      <td className="px-4 py-2.5 font-medium text-slate-900">{user.username}</td>
      <td className="px-4 py-2.5 text-slate-600">{user.email}</td>
      <td className="px-4 py-2.5 text-slate-600">{user.roles.join(', ') || '—'}</td>
      <td className="px-4 py-2.5 text-slate-500">{user.lastLoginAt ? formatRelative(user.lastLoginAt) : 'Never'}</td>
      <td className="px-4 py-2.5">
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${user.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {user.isActive ? 'Active' : 'Inactive'}
        </span>
      </td>
      {canManage && (
        <td className="px-4 py-2.5">
          <ActionButton
            label={user.isActive ? 'Deactivate' : 'Activate'}
            variant={user.isActive ? 'danger' : 'secondary'}
            confirmLabel={user.isActive ? 'Confirm deactivate' : undefined}
            onAction={() =>
              UsersApi.update(user.id, { email: user.email, fullName: user.fullName, isActive: !user.isActive, roleIds })
            }
            onSuccess={onChanged}
          />
        </td>
      )}
    </tr>
  );
}

function CreateUserForm({ roles, onCreated }: { roles: RoleDto[]; onCreated: () => void }) {
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);

  const canSubmit = username.trim() && email.trim() && fullName.trim() && password.length >= 8 && selectedRoleIds.length > 0;

  function toggleRole(id: string) {
    setSelectedRoleIds((prev) => (prev.includes(id) ? prev.filter((r) => r !== id) : [...prev, id]));
  }

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New user</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="block text-xs font-medium text-slate-600">
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Full name
          <input value={fullName} onChange={(e) => setFullName(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} className={inputClass} placeholder="At least 8 characters" />
        </label>
      </div>
      <div className="mt-3">
        <p className="text-xs font-medium text-slate-600">Roles</p>
        <div className="mt-1 flex flex-wrap gap-2">
          {roles.map((role) => (
            <label key={role.id} className="flex items-center gap-1.5 rounded-md border border-slate-300 px-2 py-1 text-xs">
              <input type="checkbox" checked={selectedRoleIds.includes(role.id)} onChange={() => toggleRole(role.id)} />
              {role.name}
            </label>
          ))}
        </div>
      </div>
      <div className="mt-3">
        <ActionButton
          label="Create user"
          disabled={!canSubmit}
          disabledReason="Fill in every field, use a password of at least 8 characters, and select at least one role."
          onAction={() =>
            UsersApi.create({ username: username.trim(), email: email.trim(), fullName: fullName.trim(), password, roleIds: selectedRoleIds })
          }
          onSuccess={onCreated}
        />
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
