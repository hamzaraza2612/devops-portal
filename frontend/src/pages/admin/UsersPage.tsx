import { useMemo, useState } from 'react';
import { EnvironmentsApi, UsersApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import type { EnvironmentDefinitionDto, UserDto } from '../../types/api';
import { formatRelative } from '../../utils/format';

async function loadUsers() {
  const [users, environments] = await Promise.all([UsersApi.list(), EnvironmentsApi.list()]);
  return { users, environments };
}

/** Phase 12 replaced the Role/Permission catalog with a simplified model
 * (see PROJECT_STATE.md): every user is either an admin (full access) or
 * holds explicit access to specific environments (DEV/QA/UAT/PRODUCTION),
 * plus an independent CanApproveProduction ("CTO approver") flag. Server-side
 * authorization is enforced from exactly these fields — this page only
 * decides what to offer, never what is allowed. */
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
        subtitle="Users and which environments each one can access. A user without access to an environment is denied server-side, not just hidden in the UI."
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
        <UserForm
          environments={data.environments}
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.users.length === 0 ? (
        <EmptyState title="No users yet." />
      ) : (
        <div className="space-y-3">
          {data.users.map((user) => (
            <UserCard key={user.id} user={user} environments={data.environments} canManage={can(Permissions.UsersManage)} onChanged={reload} />
          ))}
        </div>
      )}
    </div>
  );
}

function UserCard({
  user,
  environments,
  canManage,
  onChanged,
}: {
  user: UserDto;
  environments: EnvironmentDefinitionDto[];
  canManage: boolean;
  onChanged: () => void;
}) {
  const [showEdit, setShowEdit] = useState(false);

  return (
    <Card className={user.isActive ? '' : 'opacity-60'}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="font-medium text-slate-900">{user.username}</p>
          <p className="text-xs text-slate-500">{user.email}</p>
        </div>
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${user.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {user.isActive ? 'Active' : 'Inactive'}
        </span>
      </div>

      <div className="mt-2 flex flex-wrap gap-1.5">
        {user.isAdmin && <Badge label="Admin" color="bg-indigo-100 text-indigo-700" />}
        {user.canApproveProduction && <Badge label="Production Approver" color="bg-purple-100 text-purple-700" />}
        {user.environmentAccess.map((e) => (
          <Badge key={e.id} label={e.name} color="bg-slate-100 text-slate-700" />
        ))}
        {!user.isAdmin && user.environmentAccess.length === 0 && <span className="text-xs text-slate-400">No environment access</span>}
      </div>

      <p className="mt-2 text-xs text-slate-500">Last login: {user.lastLoginAt ? formatRelative(user.lastLoginAt) : 'Never'}</p>

      {canManage && (
        <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3">
          <button
            type="button"
            onClick={() => setShowEdit((v) => !v)}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
          >
            {showEdit ? 'Cancel' : 'Edit access'}
          </button>
          <ActionButton
            label={user.isActive ? 'Deactivate' : 'Activate'}
            variant={user.isActive ? 'danger' : 'secondary'}
            confirmLabel={user.isActive ? 'Confirm deactivate' : undefined}
            onAction={() =>
              UsersApi.update(user.id, {
                email: user.email,
                fullName: user.fullName,
                isActive: !user.isActive,
                isAdmin: user.isAdmin,
                canApproveProduction: user.canApproveProduction,
                environmentDefinitionIds: user.environmentAccess.map((e) => e.id),
              })
            }
            onSuccess={onChanged}
          />
        </div>
      )}

      {showEdit && canManage && (
        <UserAccessEditor
          user={user}
          environments={environments}
          onSaved={() => {
            setShowEdit(false);
            onChanged();
          }}
        />
      )}
    </Card>
  );
}

function Badge({ label, color }: { label: string; color: string }) {
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${color}`}>{label}</span>;
}

function UserAccessEditor({
  user,
  environments,
  onSaved,
}: {
  user: UserDto;
  environments: EnvironmentDefinitionDto[];
  onSaved: () => void;
}) {
  const [isAdmin, setIsAdmin] = useState(user.isAdmin);
  const [canApproveProduction, setCanApproveProduction] = useState(user.canApproveProduction);
  const [envIds, setEnvIds] = useState<string[]>(user.environmentAccess.map((e) => e.id));

  function toggleEnv(id: string) {
    setEnvIds((prev) => (prev.includes(id) ? prev.filter((e) => e !== id) : [...prev, id]));
  }

  return (
    <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
      <label className="flex items-center gap-1.5 text-xs font-medium text-slate-700">
        <input type="checkbox" checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
        Admin (full access to everything)
      </label>
      <label className="flex items-center gap-1.5 text-xs font-medium text-slate-700">
        <input type="checkbox" checked={canApproveProduction} onChange={(e) => setCanApproveProduction(e.target.checked)} />
        Production approver (CTO — can approve, never deploys)
      </label>

      {!isAdmin && (
        <div>
          <p className="text-xs font-medium text-slate-600">Environment access</p>
          <div className="mt-1 flex flex-wrap gap-2">
            {environments.map((env) => (
              <label key={env.id} className="flex items-center gap-1.5 rounded-md border border-slate-300 px-2 py-1 text-xs">
                <input type="checkbox" checked={envIds.includes(env.id)} onChange={() => toggleEnv(env.id)} />
                {env.name}
              </label>
            ))}
          </div>
        </div>
      )}

      <ActionButton
        label="Save access"
        onAction={() =>
          UsersApi.update(user.id, {
            email: user.email,
            fullName: user.fullName,
            isActive: user.isActive,
            isAdmin,
            canApproveProduction,
            environmentDefinitionIds: isAdmin ? [] : envIds,
          })
        }
        onSuccess={onSaved}
      />
    </div>
  );
}

function UserForm({ environments, onSubmitted }: { environments: EnvironmentDefinitionDto[]; onSubmitted: () => void }) {
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [canApproveProduction, setCanApproveProduction] = useState(false);
  const [envIds, setEnvIds] = useState<string[]>([]);

  const canSubmit = useMemo(
    () => username.trim() && email.trim() && fullName.trim() && password.length >= 8,
    [username, email, fullName, password],
  );

  function toggleEnv(id: string) {
    setEnvIds((prev) => (prev.includes(id) ? prev.filter((e) => e !== id) : [...prev, id]));
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

      <div className="mt-3 space-y-2">
        <label className="flex items-center gap-1.5 text-xs font-medium text-slate-700">
          <input type="checkbox" checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
          Admin (full access to everything)
        </label>
        <label className="flex items-center gap-1.5 text-xs font-medium text-slate-700">
          <input type="checkbox" checked={canApproveProduction} onChange={(e) => setCanApproveProduction(e.target.checked)} />
          Production approver (CTO — can approve, never deploys)
        </label>

        {!isAdmin && (
          <div>
            <p className="text-xs font-medium text-slate-600">Environment access</p>
            <div className="mt-1 flex flex-wrap gap-2">
              {environments.map((env) => (
                <label key={env.id} className="flex items-center gap-1.5 rounded-md border border-slate-300 px-2 py-1 text-xs">
                  <input type="checkbox" checked={envIds.includes(env.id)} onChange={() => toggleEnv(env.id)} />
                  {env.name}
                </label>
              ))}
            </div>
          </div>
        )}
      </div>

      <div className="mt-3">
        <ActionButton
          label="Create user"
          disabled={!canSubmit}
          disabledReason="Fill in every field with a password of at least 8 characters."
          onAction={() =>
            UsersApi.create({
              username: username.trim(),
              email: email.trim(),
              fullName: fullName.trim(),
              password,
              isAdmin,
              canApproveProduction,
              environmentDefinitionIds: isAdmin ? [] : envIds,
            })
          }
          onSuccess={onSubmitted}
        />
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
