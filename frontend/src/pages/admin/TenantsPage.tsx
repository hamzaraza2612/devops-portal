import { useRef, useState } from 'react';
import { TenantsApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import type { CreateTenantResponse, TenantDto } from '../../types/api';
import { formatDateTime } from '../../utils/format';

/** Platform-administrator only — see PermissionCodes.TenantsView/TenantsManage.
 * A tenant's own ADMIN role can never reach this page (RequirePermission
 * gates the route, and the API rejects it server-side regardless). */
export function TenantsPage() {
  const { data, isLoading, error, reload } = useAsyncData(TenantsApi.list, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);
  const [lastCreated, setLastCreated] = useState<CreateTenantResponse | null>(null);

  if (isLoading) return <LoadingSpinner label="Loading tenants…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;

  return (
    <div>
      <PageHeader
        title="Tenants"
        subtitle="Organizations using this platform. Each tenant's applications, users, roles, and deployments are fully isolated from every other tenant."
        actions={
          can(Permissions.TenantsManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Tenant'}
            </button>
          )
        }
      />

      {lastCreated && (
        <Card className="mb-4 border-emerald-200 bg-emerald-50">
          <p className="text-sm font-medium text-emerald-900">
            Tenant "{lastCreated.tenant.name}" created.
          </p>
          {lastCreated.generatedPassword ? (
            <p className="mt-1 text-sm text-emerald-800">
              Initial admin <span className="font-mono">{lastCreated.initialAdminUsername}</span> — generated password:{' '}
              <span className="font-mono font-semibold">{lastCreated.generatedPassword}</span>. This is shown once — copy it now.
            </p>
          ) : (
            <p className="mt-1 text-sm text-emerald-800">
              Initial admin <span className="font-mono">{lastCreated.initialAdminUsername}</span> was created with the password you supplied.
            </p>
          )}
          <button type="button" onClick={() => setLastCreated(null)} className="mt-2 text-xs font-medium text-emerald-700 hover:underline">
            Dismiss
          </button>
        </Card>
      )}

      {showCreate && (
        <CreateTenantForm
          onCreated={(response) => {
            setLastCreated(response);
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {!data || data.length === 0 ? (
        <EmptyState title="No tenants yet." hint="Create the first tenant to get started." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Name</th>
                <th className="px-4 py-2">Slug</th>
                <th className="px-4 py-2">Description</th>
                <th className="px-4 py-2">Status</th>
                <th className="px-4 py-2">Created</th>
                {can(Permissions.TenantsManage) && <th className="px-4 py-2" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.map((tenant) => (
                <TenantRow key={tenant.id} tenant={tenant} canManage={can(Permissions.TenantsManage)} onChanged={reload} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function TenantRow({ tenant, canManage, onChanged }: { tenant: TenantDto; canManage: boolean; onChanged: () => void }) {
  return (
    <tr className={tenant.isActive ? '' : 'opacity-60'}>
      <td className="px-4 py-2.5 font-medium text-slate-900">{tenant.name}</td>
      <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{tenant.slug}</td>
      <td className="px-4 py-2.5 text-slate-600">{tenant.description ?? '—'}</td>
      <td className="px-4 py-2.5">
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tenant.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {tenant.isActive ? 'Active' : 'Inactive'}
        </span>
      </td>
      <td className="px-4 py-2.5 text-slate-500">{formatDateTime(tenant.createdAt)}</td>
      {canManage && (
        <td className="px-4 py-2.5">
          <ActionButton
            label={tenant.isActive ? 'Deactivate' : 'Activate'}
            variant={tenant.isActive ? 'danger' : 'secondary'}
            confirmLabel={tenant.isActive ? 'Confirm deactivate' : undefined}
            onAction={() => TenantsApi.update(tenant.id, { name: tenant.name, description: tenant.description, isActive: !tenant.isActive })}
            onSuccess={onChanged}
          />
        </td>
      )}
    </tr>
  );
}

function CreateTenantForm({ onCreated }: { onCreated: (response: CreateTenantResponse) => void }) {
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [adminUsername, setAdminUsername] = useState('');
  const [adminEmail, setAdminEmail] = useState('');
  const [adminPassword, setAdminPassword] = useState('');
  const resultRef = useRef<CreateTenantResponse | null>(null);

  const canSubmit = name.trim() && slug.trim() && adminUsername.trim() && adminEmail.trim();

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New tenant</h2>
      <p className="mt-1 text-xs text-slate-500">
        Provisions a new organization with its own default roles (ADMIN/DEVOPS/DEVELOPER/QA/UAT/CTO), pipeline environments (DEV/QA/UAT/PRODUCTION), and one
        initial administrator who can sign in immediately.
      </p>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <Field label="Organization name">
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="Acme Corp" />
        </Field>
        <Field label="Slug">
          <input value={slug} onChange={(e) => setSlug(e.target.value)} className={inputClass} placeholder="acme-corp" />
        </Field>
        <Field label="Description" className="sm:col-span-2">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Initial admin username">
          <input value={adminUsername} onChange={(e) => setAdminUsername(e.target.value)} className={inputClass} />
        </Field>
        <Field label="Initial admin email">
          <input type="email" value={adminEmail} onChange={(e) => setAdminEmail(e.target.value)} className={inputClass} />
        </Field>
        <Field label="Initial admin password" className="sm:col-span-2">
          <input
            type="password"
            value={adminPassword}
            onChange={(e) => setAdminPassword(e.target.value)}
            className={inputClass}
            placeholder="Leave blank to auto-generate"
          />
        </Field>
      </div>
      <div className="mt-3">
        <ActionButton
          label="Create tenant"
          disabled={!canSubmit}
          disabledReason="Fill in every required field first."
          onAction={async () => {
            const response = await TenantsApi.create({
              name: name.trim(),
              slug: slug.trim(),
              description: description.trim() || null,
              initialAdminUsername: adminUsername.trim(),
              initialAdminEmail: adminEmail.trim(),
              initialAdminPassword: adminPassword.trim() || null,
            });
            resultRef.current = response;
          }}
          onSuccess={() => {
            if (resultRef.current) onCreated(resultRef.current);
          }}
        />
      </div>
    </Card>
  );
}

function Field({ label, className = '', children }: { label: string; className?: string; children: React.ReactNode }) {
  return (
    <label className={`block text-xs font-medium text-slate-600 ${className}`}>
      {label}
      <div className="mt-1">{children}</div>
    </label>
  );
}

const inputClass = 'w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
