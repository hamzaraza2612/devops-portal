import { useState } from 'react';
import { RepositoriesApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import { RepositoryProvider, type RepositoryConnectionTestResultDto, type RepositoryDto } from '../../types/api';
import { describeError } from '../../api/client';

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
        title="Repositories / GitLab"
        subtitle="GitLab repository connections your applications build and deploy from. The access token is stored encrypted — never in Git, never returned by any API."
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
        <div className="space-y-3">
          {data.map((repo) => (
            <RepositoryCard key={repo.id} repo={repo} canManage={can(Permissions.RepositoriesManage)} onChanged={reload} />
          ))}
        </div>
      )}
    </div>
  );
}

function RepositoryCard({ repo, canManage, onChanged }: { repo: RepositoryDto; canManage: boolean; onChanged: () => void }) {
  const [testResult, setTestResult] = useState<RepositoryConnectionTestResultDto | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const [isTesting, setIsTesting] = useState(false);
  const [showTokenForm, setShowTokenForm] = useState(false);
  const [token, setToken] = useState('');

  async function runTest() {
    setIsTesting(true);
    setTestError(null);
    try {
      setTestResult(await RepositoriesApi.testConnection(repo.id));
    } catch (err) {
      setTestError(describeError(err));
      setTestResult(null);
    } finally {
      setIsTesting(false);
    }
  }

  return (
    <Card className={repo.isActive ? '' : 'opacity-60'}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-medium text-slate-900">{repo.name}</h3>
          <p className="break-all text-xs text-slate-500">{repo.url}</p>
        </div>
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${repo.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {repo.isActive ? 'Active' : 'Inactive'}
        </span>
      </div>

      <dl className="mt-3 grid gap-1 text-xs sm:grid-cols-2">
        <Row label="Default branch" value={repo.defaultBranch ?? '—'} />
        <Row label="Username" value={repo.username ?? '—'} />
        <Row label="Access token" value={repo.hasAccessToken ? 'Configured' : repo.accessTokenEnvVarName ? `env: ${repo.accessTokenEnvVarName}` : 'Not configured'} />
      </dl>

      <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3">
        <button
          type="button"
          onClick={() => void runTest()}
          disabled={isTesting}
          className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-60"
        >
          {isTesting ? 'Testing…' : 'Test GitLab Connection'}
        </button>
        {canManage && (
          <button
            type="button"
            onClick={() => setShowTokenForm((v) => !v)}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
          >
            {showTokenForm ? 'Cancel' : 'Set access token'}
          </button>
        )}
        {canManage && (
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
                defaultBranch: repo.defaultBranch,
                username: repo.username,
                accessTokenEnvVarName: repo.accessTokenEnvVarName,
                isActive: !repo.isActive,
              })
            }
            onSuccess={onChanged}
          />
        )}
      </div>

      {testError && <p className="mt-2 text-xs text-red-600">{testError}</p>}
      {testResult && (
        <p className={`mt-2 text-xs font-medium ${testResult.connected ? 'text-emerald-700' : 'text-red-600'}`}>
          {testResult.connected
            ? `CONNECTED to ${testResult.projectName ?? repo.name}${testResult.authenticatedAs ? ` as ${testResult.authenticatedAs}` : ''}`
            : `FAILED: ${testResult.errorMessage}`}
        </p>
      )}

      {showTokenForm && canManage && (
        <div className="mt-3 flex flex-wrap items-end gap-2 border-t border-slate-100 pt-3">
          <label className="block text-xs font-medium text-slate-600">
            GitLab access token
            <input
              type="password"
              value={token}
              onChange={(e) => setToken(e.target.value)}
              className={inputClass}
              placeholder="Paste token — never displayed again"
            />
          </label>
          <ActionButton
            label="Save token"
            disabled={!token.trim()}
            onAction={() => RepositoriesApi.setAccessToken(repo.id, { value: token.trim() })}
            onSuccess={() => {
              setToken('');
              setShowTokenForm(false);
              onChanged();
            }}
          />
        </div>
      )}
    </Card>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-slate-500">{label}</dt>
      <dd className="font-medium text-slate-800">{value}</dd>
    </div>
  );
}

function RepositoryForm({ onSubmitted }: { onSubmitted: () => void }) {
  const [name, setName] = useState('');
  const [url, setUrl] = useState('');
  const [description, setDescription] = useState('');
  const [defaultBranch, setDefaultBranch] = useState('');
  const [username, setUsername] = useState('');
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
          GitLab URL
          <input value={url} onChange={(e) => setUrl(e.target.value)} className={inputClass} placeholder="https://gitlab.example.com/group/app.git" />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Default branch
          <input value={defaultBranch} onChange={(e) => setDefaultBranch(e.target.value)} className={inputClass} placeholder="main" />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} placeholder="GitLab username or service account" />
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Legacy: access token environment variable name
          <input
            value={accessTokenEnvVarName}
            onChange={(e) => setAccessTokenEnvVarName(e.target.value)}
            className={inputClass}
            placeholder="Optional — prefer setting the access token directly after creating"
          />
        </label>
      </div>
      <p className="mt-2 text-xs text-slate-400">
        After creating this repository, use &ldquo;Set access token&rdquo; to store the GitLab access token securely, then &ldquo;Test GitLab
        Connection&rdquo; to verify it.
      </p>
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
              defaultBranch: defaultBranch.trim() || null,
              username: username.trim() || null,
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
