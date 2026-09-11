import { useState } from 'react';
import { RolesApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import type { PermissionDto, RoleDto } from '../../types/api';

async function loadRoles() {
  const [roles, permissions] = await Promise.all([RolesApi.list(), RolesApi.permissions()]);
  return { roles, permissions };
}

/** Tenant-scoped: this list only ever shows the current tenant's own roles
 * (plus system-wide roles, none of which are ever assignable here — see
 * RoleService.CreateAsync/UpdateAsync, both of which reject IsSystem rows). */
export function RolesPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadRoles, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);
  const [editingRoleId, setEditingRoleId] = useState<string | null>(null);

  if (isLoading) return <LoadingSpinner label="Loading roles…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Roles"
        subtitle="Organization-specific roles and the permissions each one grants. The default roles (ADMIN, DEVOPS, DEVELOPER, QA, UAT, CTO) are system-provisioned and read-only; create a custom role for anything else your organization needs."
        actions={
          can(Permissions.RolesManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Role'}
            </button>
          )
        }
      />

      {showCreate && (
        <RoleForm
          permissions={data.permissions}
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.roles.length === 0 ? (
        <EmptyState title="No roles yet." />
      ) : (
        <div className="space-y-3">
          {data.roles.map((role) =>
            editingRoleId === role.id ? (
              <RoleForm
                key={role.id}
                role={role}
                permissions={data.permissions}
                onSubmitted={() => {
                  setEditingRoleId(null);
                  reload();
                }}
                onCancel={() => setEditingRoleId(null)}
              />
            ) : (
              <Card key={role.id}>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="text-sm font-semibold text-slate-900">
                      {role.name}
                      {role.isSystem && <span className="ml-2 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-500">System</span>}
                    </h3>
                    <p className="mt-0.5 text-xs text-slate-500">{role.description}</p>
                  </div>
                  {can(Permissions.RolesManage) && !role.isSystem && (
                    <button type="button" onClick={() => setEditingRoleId(role.id)} className="shrink-0 text-xs font-medium text-slate-600 hover:underline">
                      Edit permissions
                    </button>
                  )}
                </div>
                <div className="mt-2 flex flex-wrap gap-1.5">
                  {role.permissions.length === 0 ? (
                    <span className="text-xs text-slate-400">No permissions granted.</span>
                  ) : (
                    role.permissions.map((code) => (
                      <span key={code} className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[11px] text-slate-600">
                        {code}
                      </span>
                    ))
                  )}
                </div>
              </Card>
            ),
          )}
        </div>
      )}
    </div>
  );
}

function RoleForm({
  role,
  permissions,
  onSubmitted,
  onCancel,
}: {
  role?: RoleDto;
  permissions: PermissionDto[];
  onSubmitted: () => void;
  onCancel?: () => void;
}) {
  const [name, setName] = useState(role?.name ?? '');
  const [description, setDescription] = useState(role?.description ?? '');
  const [selected, setSelected] = useState<string[]>(role ? permissions.filter((p) => role.permissions.includes(p.code)).map((p) => p.id) : []);

  const isEdit = Boolean(role);
  const canSubmit = isEdit ? description.trim().length > 0 : name.trim().length > 0 && description.trim().length > 0;

  function toggle(id: string) {
    setSelected((prev) => (prev.includes(id) ? prev.filter((p) => p !== id) : [...prev, id]));
  }

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">{isEdit ? `Edit "${role?.name}"` : 'New role'}</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        {!isEdit && (
          <label className="block text-xs font-medium text-slate-600">
            Role name
            <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="e.g. RELEASE_MANAGER" />
          </label>
        )}
        <label className={`block text-xs font-medium text-slate-600 ${isEdit ? 'sm:col-span-2' : ''}`}>
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} />
        </label>
      </div>
      <div className="mt-3">
        <p className="text-xs font-medium text-slate-600">Permissions</p>
        <div className="mt-1 grid max-h-64 gap-1 overflow-y-auto rounded-md border border-slate-200 p-2 sm:grid-cols-2">
          {permissions.map((permission) => (
            <label key={permission.id} className="flex items-start gap-1.5 text-xs text-slate-700">
              <input type="checkbox" className="mt-0.5" checked={selected.includes(permission.id)} onChange={() => toggle(permission.id)} />
              <span>
                <span className="font-mono">{permission.code}</span>
                <span className="block text-[11px] text-slate-400">{permission.description}</span>
              </span>
            </label>
          ))}
        </div>
      </div>
      <div className="mt-3 flex items-center gap-2">
        <ActionButton
          label={isEdit ? 'Save permissions' : 'Create role'}
          disabled={!canSubmit}
          disabledReason="Fill in the required fields first."
          onAction={() =>
            isEdit && role
              ? RolesApi.update(role.id, { description: description.trim(), permissionIds: selected })
              : RolesApi.create({ name: name.trim(), description: description.trim(), permissionIds: selected })
          }
          onSuccess={onSubmitted}
        />
        {onCancel && (
          <button type="button" onClick={onCancel} className="text-sm text-slate-500 hover:text-slate-700">
            Cancel
          </button>
        )}
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
