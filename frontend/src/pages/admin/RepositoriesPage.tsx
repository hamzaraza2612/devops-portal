import { useState } from 'react';
import { RepositoriesApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import { RepositoryProvider, type RepositoryDto } from '../../types/api';

export function RepositoriesPage() {
  const { data, isLoading, error, reload } = useAsyncData(RepositoriesApi.list, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);

  if (isLoading) return <LoadingSpinner label="Loading repositories…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Repositories"
        subtitle="Git repository references your applications build and deploy from. Never stores credentials — only the name of a server-side environment variable holding an access token."
        actions={
          can(Permissions.RepositoriesManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Repository'}
            </button>
          )
        }
      />

      {showCreate && (
        <RepositoryForm
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.length === 0 ? (
        <EmptyState title="No repositories configured yet." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Name</th>
                <th className="px-4 py-2">URL</th>
                <th className="px-4 py-2">Access token env var</th>
                <th className="px-4 py-2">Status</th>
                {can(Permissions.RepositoriesManage) && <th className="px-4 py-2" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.map((repo) => (
                <RepositoryRow key={repo.id} repo={repo} canManage={can(Permissions.RepositoriesManage)} onChanged={reload} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function RepositoryRow({ repo, canManage, onChanged }: { repo: RepositoryDto; canManage: boolean; onChanged: () => void }) {
  return (
    <tr className={repo.isActive ? '' : 'opacity-60'}>
      <td className="px-4 py-2.5 font-medium text-slate-900">{repo.name}</td>
      <td className="px-4 py-2.5 text-slate-600">
        <span className="break-all">{repo.url}</span>
      </td>
      <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{repo.accessTokenEnvVarName ?? '—'}</td>
      <td className="px-4 py-2.5">
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${repo.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {repo.isActive ? 'Active' : 'Inactive'}
        </span>
      </td>
      {canManage && (
        <td className="px-4 py-2.5">
          <ActionButton
            label={repo.isActive ? 'Deactivate' : 'Activate'}
            variant={repo.isActive ? 'danger' : 'secondary'}
            confirmLabel={repo.isActive ? 'Confirm deactivate' : undefined}
            onAction={() =>
              RepositoriesApi.update(repo.id, {
                name: repo.name,
                url: repo.url,
                provider: repo.provider,
                description: repo.description,
                accessTokenEnvVarName: repo.accessTokenEnvVarName,
                isActive: !repo.isActive,
              })
            }
            onSuccess={onChanged}
          />
        </td>
      )}
    </tr>
  );
}

function RepositoryForm({ onSubmitted }: { onSubmitted: () => void }) {
  const [name, setName] = useState('');
  const [url, setUrl] = useState('');
  const [description, setDescription] = useState('');
  const [accessTokenEnvVarName, setAccessTokenEnvVarName] = useState('');

  const canSubmit = name.trim() && url.trim();

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New repository</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="block text-xs font-medium text-slate-600">
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          URL
          <input value={url} onChange={(e) => setUrl(e.target.value)} className={inputClass} placeholder="https://gitlab.example.com/group/app.git" />
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Access token environment variable name
          <input
            value={accessTokenEnvVarName}
            onChange={(e) => setAccessTokenEnvVarName(e.target.value)}
            className={inputClass}
            placeholder="e.g. GITLAB_TOKEN_MYAPP — never the token itself"
          />
        </label>
      </div>
      <div className="mt-3">
        <ActionButton
          label="Create repository"
          disabled={!canSubmit}
          disabledReason="Name and URL are required."
          onAction={() =>
            RepositoriesApi.create({
              name: name.trim(),
              url: url.trim(),
              provider: RepositoryProvider.GitLab,
              description: description.trim() || null,
              accessTokenEnvVarName: accessTokenEnvVarName.trim() || null,
            })
          }
          onSuccess={onSubmitted}
        />
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
