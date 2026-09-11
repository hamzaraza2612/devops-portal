import { useState } from 'react';
import { BuildServersApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import { BuildProviderType, type BuildServerDto } from '../../types/api';

/** "Integrations": build-provider connections (Jenkins today; the
 * IBuildProvider abstraction supports adding others without touching this
 * page). Never stores or returns a credential — only the name of a
 * server-side environment variable holding the API token. */
export function BuildServersPage() {
  const { data, isLoading, error, reload } = useAsyncData(BuildServersApi.list, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);

  if (isLoading) return <LoadingSpinner label="Loading integrations…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Integrations"
        subtitle="Configured build servers (e.g. Jenkins instances) applications can build from source through."
        actions={
          can(Permissions.BuildServersManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Build Server'}
            </button>
          )
        }
      />

      {showCreate && (
        <BuildServerForm
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.length === 0 ? (
        <EmptyState title="No build servers configured yet." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Name</th>
                <th className="px-4 py-2">Base URL</th>
                <th className="px-4 py-2">API token env var</th>
                <th className="px-4 py-2">Status</th>
                {can(Permissions.BuildServersManage) && <th className="px-4 py-2" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.map((server) => (
                <BuildServerRow key={server.id} server={server} canManage={can(Permissions.BuildServersManage)} onChanged={reload} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function BuildServerRow({ server, canManage, onChanged }: { server: BuildServerDto; canManage: boolean; onChanged: () => void }) {
  return (
    <tr className={server.isActive ? '' : 'opacity-60'}>
      <td className="px-4 py-2.5 font-medium text-slate-900">{server.name}</td>
      <td className="px-4 py-2.5 text-slate-600">
        <span className="break-all">{server.baseUrl}</span>
      </td>
      <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{server.apiTokenEnvVarName ?? '—'}</td>
      <td className="px-4 py-2.5">
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${server.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {server.isActive ? 'Active' : 'Inactive'}
        </span>
      </td>
      {canManage && (
        <td className="px-4 py-2.5">
          <ActionButton
            label={server.isActive ? 'Deactivate' : 'Activate'}
            variant={server.isActive ? 'danger' : 'secondary'}
            confirmLabel={server.isActive ? 'Confirm deactivate' : undefined}
            onAction={() =>
              BuildServersApi.update(server.id, {
                name: server.name,
                description: server.description,
                baseUrl: server.baseUrl,
                username: server.username,
                apiTokenEnvVarName: server.apiTokenEnvVarName,
                isActive: !server.isActive,
              })
            }
            onSuccess={onChanged}
          />
        </td>
      )}
    </tr>
  );
}

function BuildServerForm({ onSubmitted }: { onSubmitted: () => void }) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [baseUrl, setBaseUrl] = useState('');
  const [username, setUsername] = useState('');
  const [apiTokenEnvVarName, setApiTokenEnvVarName] = useState('');

  const canSubmit = name.trim() && baseUrl.trim();

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New build server</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="block text-xs font-medium text-slate-600">
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Base URL
          <input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} className={inputClass} placeholder="https://jenkins.example.com" />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          API token environment variable name
          <input
            value={apiTokenEnvVarName}
            onChange={(e) => setApiTokenEnvVarName(e.target.value)}
            className={inputClass}
            placeholder="e.g. JENKINS_TOKEN_MAIN"
          />
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
      </div>
      <div className="mt-3">
        <ActionButton
          label="Create build server"
          disabled={!canSubmit}
          disabledReason="Name and base URL are required."
          onAction={() =>
            BuildServersApi.create({
              name: name.trim(),
              description: description.trim() || null,
              providerType: BuildProviderType.Jenkins,
              baseUrl: baseUrl.trim(),
              username: username.trim() || null,
              apiTokenEnvVarName: apiTokenEnvVarName.trim() || null,
            })
          }
          onSuccess={onSubmitted}
        />
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
