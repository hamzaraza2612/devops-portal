import { useRef, useState } from 'react';
import { ApplicationsApi, EnvironmentsApi, SecretsApi } from '../api/endpoints';
import { ActionButton } from '../components/ActionButton';
import { Can, Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { useAsyncData } from '../hooks/useAsyncData';
import { Permissions } from '../auth/permissions';
import { useAuth } from '../auth/AuthContext';
import { SecretCategory, SecretScope, type ApplicationDto, type EnvironmentDefinitionDto, type SecretReferenceDto } from '../types/api';

const categoryLabels: Record<SecretCategory, string> = {
  [SecretCategory.Database]: 'Database',
  [SecretCategory.Api]: 'API',
  [SecretCategory.Registry]: 'Registry',
  [SecretCategory.GitLab]: 'GitLab',
  [SecretCategory.Jenkins]: 'Jenkins',
  [SecretCategory.Smtp]: 'SMTP',
  [SecretCategory.Server]: 'Server',
  [SecretCategory.Other]: 'Other',
};

async function loadCredentials() {
  const [secrets, applications, environments] = await Promise.all([SecretsApi.list(), ApplicationsApi.list(), EnvironmentsApi.list()]);
  return { secrets, applications, environments };
}

/** "Credentials" — application/database logins operators frequently need
 * without asking DevOps directly (portal usernames, DB passwords, etc). Same
 * backend model as Secrets (SecretReference) with structured display fields;
 * the value itself is never fetched until a viewer explicitly clicks "Show
 * password", and only ever for one record at a time. */
export function CredentialsPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadCredentials, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);

  if (isLoading) return <LoadingSpinner label="Loading credentials…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Credentials"
        subtitle="Application and database logins for the environments you're authorized to see. Passwords stay hidden until you explicitly reveal one."
        actions={
          <Can permission={Permissions.SecretsManage}>
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Credential'}
            </button>
          </Can>
        }
      />

      {showCreate && (
        <CredentialForm
          applications={data.applications}
          environments={data.environments}
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.secrets.length === 0 ? (
        <EmptyState title="No credentials recorded yet." />
      ) : (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {data.secrets.map((secret) => (
            <CredentialCard key={secret.id} secret={secret} canManage={can(Permissions.SecretsManage)} canReveal={can(Permissions.SecretsReveal)} onChanged={reload} />
          ))}
        </div>
      )}
    </div>
  );
}

function CredentialCard({
  secret,
  canManage,
  canReveal,
  onChanged,
}: {
  secret: SecretReferenceDto;
  canManage: boolean;
  canReveal: boolean;
  onChanged: () => void;
}) {
  const [revealedValue, setRevealedValue] = useState<string | null>(null);
  const revealedRef = useRef<string | null>(null);
  const [showEditForm, setShowEditForm] = useState(false);
  const [showRotateForm, setShowRotateForm] = useState(false);
  const [newValue, setNewValue] = useState('');

  return (
    <Card className={secret.isActive ? '' : 'opacity-60'}>
      <div className="flex items-start justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold text-slate-900">{secret.name}</h3>
          <p className="mt-0.5 text-xs text-slate-500">
            {categoryLabels[secret.category]}
            {secret.applicationName && ` · ${secret.applicationName}`}
            {secret.environmentName && ` · ${secret.environmentName}`}
          </p>
        </div>
        {!secret.isActive && <span className="shrink-0 rounded-full bg-slate-200 px-2 py-0.5 text-[11px] font-medium text-slate-600">Inactive</span>}
      </div>

      {secret.description && <p className="mt-2 text-xs text-slate-500">{secret.description}</p>}

      <dl className="mt-3 space-y-1 text-xs">
        {secret.username && <Row label="Username" value={secret.username} />}
        {secret.host && <Row label="Host" value={secret.port ? `${secret.host}:${secret.port}` : secret.host} />}
        {secret.databaseName && <Row label="Database" value={secret.databaseName} />}
      </dl>

      <div className="mt-3 border-t border-slate-100 pt-3">
        {revealedValue !== null ? (
          <div className="flex items-center gap-2">
            <code className="flex-1 truncate rounded bg-slate-100 px-2 py-1 text-xs">{revealedValue}</code>
            <button type="button" onClick={() => setRevealedValue(null)} className="text-xs font-medium text-slate-500 hover:text-slate-700">
              Hide
            </button>
          </div>
        ) : canReveal ? (
          <ActionButton
            label="Show password"
            variant="secondary"
            onAction={async () => {
              revealedRef.current = (await SecretsApi.reveal(secret.id)).value;
            }}
            onSuccess={() => setRevealedValue(revealedRef.current)}
          />
        ) : (
          <p className="text-xs text-slate-400">You don't have permission to reveal this value.</p>
        )}
      </div>

      {canManage && (
        <div className="mt-2 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => { setShowEditForm((v) => !v); setShowRotateForm(false); }}
            className="text-xs font-medium text-slate-500 hover:text-slate-700"
          >
            {showEditForm ? 'Cancel edit' : 'Edit'}
          </button>
          <button
            type="button"
            onClick={() => { setShowRotateForm((v) => !v); setShowEditForm(false); setNewValue(''); }}
            className="text-xs font-medium text-slate-500 hover:text-slate-700"
          >
            {showRotateForm ? 'Cancel rotate' : 'Rotate value'}
          </button>
          <ActionButton
            label={secret.isActive ? 'Deactivate' : 'Activate'}
            variant={secret.isActive ? 'danger' : 'secondary'}
            confirmLabel={secret.isActive ? 'Confirm deactivate' : undefined}
            onAction={() =>
              SecretsApi.update(secret.id, {
                description: secret.description,
                isActive: !secret.isActive,
                value: null,
                username: secret.username,
                host: secret.host,
                port: secret.port,
                databaseName: secret.databaseName,
              })
            }
            onSuccess={onChanged}
          />
          <ActionButton
            label="Delete"
            variant="danger"
            confirmLabel="Confirm delete"
            onAction={() => SecretsApi.delete(secret.id)}
            onSuccess={onChanged}
          />
        </div>
      )}

      {canManage && showEditForm && (
        <CredentialEditForm
          secret={secret}
          onCancel={() => setShowEditForm(false)}
          onSaved={() => { setShowEditForm(false); onChanged(); }}
        />
      )}

      {canManage && showRotateForm && (
        <div className="mt-3 border-t border-slate-100 pt-3">
          <Field label="New password / secret value">
            <input type="password" value={newValue} onChange={(e) => setNewValue(e.target.value)} className={inputClass} />
          </Field>
          <div className="mt-2">
            <ActionButton
              label="Save new value"
              disabled={!newValue.trim()}
              disabledReason="Enter a new value first."
              onAction={() =>
                SecretsApi.update(secret.id, {
                  description: secret.description,
                  isActive: secret.isActive,
                  value: newValue.trim(),
                  username: secret.username,
                  host: secret.host,
                  port: secret.port,
                  databaseName: secret.databaseName,
                })
              }
              onSuccess={() => { setShowRotateForm(false); setNewValue(''); onChanged(); }}
            />
          </div>
        </div>
      )}
    </Card>
  );
}

function CredentialEditForm({
  secret,
  onCancel,
  onSaved,
}: {
  secret: SecretReferenceDto;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const [description, setDescription] = useState(secret.description ?? '');
  const [username, setUsername] = useState(secret.username ?? '');
  const [host, setHost] = useState(secret.host ?? '');
  const [port, setPort] = useState(secret.port != null ? String(secret.port) : '');
  const [databaseName, setDatabaseName] = useState(secret.databaseName ?? '');

  return (
    <div className="mt-3 border-t border-slate-100 pt-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Description" className="sm:col-span-2">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Username">
          <input value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Host">
          <input value={host} onChange={(e) => setHost(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Port">
          <input type="number" value={port} onChange={(e) => setPort(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Database name">
          <input value={databaseName} onChange={(e) => setDatabaseName(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
      </div>
      <div className="mt-2 flex gap-2">
        <ActionButton
          label="Save changes"
          onAction={() =>
            SecretsApi.update(secret.id, {
              description: description.trim() || null,
              isActive: secret.isActive,
              value: null,
              username: username.trim() || null,
              host: host.trim() || null,
              port: port.trim() ? Number(port) : null,
              databaseName: databaseName.trim() || null,
            })
          }
          onSuccess={onSaved}
        />
        <button type="button" onClick={onCancel} className="text-xs font-medium text-slate-500 hover:text-slate-700">
          Cancel
        </button>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-slate-500">{label}</dt>
      <dd className="font-mono font-medium text-slate-800">{value}</dd>
    </div>
  );
}

function CredentialForm({
  applications,
  environments,
  onSubmitted,
}: {
  applications: ApplicationDto[];
  environments: EnvironmentDefinitionDto[];
  onSubmitted: () => void;
}) {
  const [name, setName] = useState('');
  const [category, setCategory] = useState<SecretCategory>(SecretCategory.Database);
  const [scope, setScope] = useState<SecretScope>(SecretScope.Global);
  const [applicationId, setApplicationId] = useState('');
  const [environmentDefinitionId, setEnvironmentDefinitionId] = useState('');
  const [description, setDescription] = useState('');
  const [username, setUsername] = useState('');
  const [host, setHost] = useState('');
  const [port, setPort] = useState('');
  const [databaseName, setDatabaseName] = useState('');
  const [value, setValue] = useState('');

  const needsApplication = scope !== SecretScope.Global;
  const needsEnvironment = scope === SecretScope.ApplicationEnvironment;
  const canSubmit = name.trim() && value.trim() && (!needsApplication || applicationId) && (!needsEnvironment || environmentDefinitionId);

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New credential</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <Field label="Name">
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="e.g. reporting-db-password" />
        </Field>
        <Field label="Category">
          <select value={category} onChange={(e) => setCategory(Number(e.target.value) as SecretCategory)} className={inputClass}>
            {Object.entries(categoryLabels).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </Field>
        <Field label="Scope">
          <select
            value={scope}
            onChange={(e) => {
              const next = Number(e.target.value) as SecretScope;
              setScope(next);
              if (next === SecretScope.Global) { setApplicationId(''); setEnvironmentDefinitionId(''); }
              if (next === SecretScope.Application) setEnvironmentDefinitionId('');
            }}
            className={inputClass}
          >
            <option value={SecretScope.Global}>Global</option>
            <option value={SecretScope.Application}>Application</option>
            <option value={SecretScope.ApplicationEnvironment}>Application + Environment</option>
          </select>
        </Field>
        {needsApplication && (
          <Field label="Application">
            <select value={applicationId} onChange={(e) => setApplicationId(e.target.value)} className={inputClass}>
              <option value="">Select…</option>
              {applications.map((app) => (
                <option key={app.id} value={app.id}>{app.name}</option>
              ))}
            </select>
          </Field>
        )}
        {needsEnvironment && (
          <Field label="Environment">
            <select value={environmentDefinitionId} onChange={(e) => setEnvironmentDefinitionId(e.target.value)} className={inputClass}>
              <option value="">Select…</option>
              {environments.map((env) => (
                <option key={env.id} value={env.id}>{env.name}</option>
              ))}
            </select>
          </Field>
        )}
        <Field label="Description" className="sm:col-span-2">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Username">
          <input value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Host">
          <input value={host} onChange={(e) => setHost(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Port">
          <input type="number" value={port} onChange={(e) => setPort(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Database name">
          <input value={databaseName} onChange={(e) => setDatabaseName(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Password / secret value" className="sm:col-span-2">
          <input type="password" value={value} onChange={(e) => setValue(e.target.value)} className={inputClass} />
        </Field>
      </div>
      <div className="mt-3">
        <ActionButton
          label="Create credential"
          disabled={!canSubmit}
          disabledReason="Fill in the required fields first."
          onAction={() =>
            SecretsApi.create({
              name: name.trim(),
              category,
              scope,
              applicationId: needsApplication ? applicationId : null,
              environmentDefinitionId: needsEnvironment ? environmentDefinitionId : null,
              description: description.trim() || null,
              username: username.trim() || null,
              host: host.trim() || null,
              port: port.trim() ? Number(port) : null,
              databaseName: databaseName.trim() || null,
              value: value.trim(),
            })
          }
          onSuccess={onSubmitted}
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
