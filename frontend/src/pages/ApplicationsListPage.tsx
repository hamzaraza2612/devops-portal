import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, RepositoriesApi } from '../api/endpoints';
import { ActionButton } from '../components/ActionButton';
import { Can, Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { useAuth } from '../auth/AuthContext';
import { EnvironmentTiers, Permissions } from '../auth/permissions';
import { DeploymentMode, type ApplicationDto, type CreateApplicationRequest, type DeploymentDto, type RepositoryDto, type UpdateApplicationRequest } from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatRelative } from '../utils/format';

const deploymentModeLabels: Record<DeploymentMode, string> = {
  [DeploymentMode.LegacyFilesystem]: 'Legacy filesystem',
  [DeploymentMode.ContainerImage]: 'Container image',
};

async function loadApplications() {
  const [applications, deployments, repositories] = await Promise.all([ApplicationsApi.list(), DeploymentsApi.list(), RepositoriesApi.list()]);
  return { applications, deployments, repositories };
}

export function ApplicationsListPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadApplications, []);
  const { can } = useAuth();
  const [search, setSearch] = useState('');
  const [showInactive, setShowInactive] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const latest = useMemo(() => (data ? latestByAppEnvironment(data.deployments) : new Map<string, DeploymentDto>()), [data]);

  const filtered = useMemo(() => {
    if (!data) return [];
    const term = search.trim().toLowerCase();
    return data.applications
      .filter((app) => showInactive || app.isActive)
      .filter((app) => !term || app.name.toLowerCase().includes(term) || app.slug.toLowerCase().includes(term) || (app.repositoryName ?? '').toLowerCase().includes(term))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [data, search, showInactive]);

  if (isLoading) return <LoadingSpinner label="Loading applications…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  const canManage = can(Permissions.ApplicationsManage);

  return (
    <div>
      <PageHeader
        title="Applications"
        subtitle="Applications you're authorized to view, with current status per environment."
        actions={
          <Can permission={Permissions.ApplicationsManage}>
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Application'}
            </button>
          </Can>
        }
      />

      {showCreate && (
        <ApplicationCreateForm
          repositories={data.repositories}
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <input
          type="search"
          placeholder="Search by name, slug, or repository…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-full max-w-sm rounded-md border border-slate-300 px-3 py-1.5 text-sm focus:border-slate-500 focus:outline-none"
        />
        <label className="flex items-center gap-1.5 text-sm text-slate-600">
          <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
          Show inactive
        </label>
      </div>

      {filtered.length === 0 ? (
        <EmptyState title="No applications match your search." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full divide-y divide-slate-200 text-sm">
            <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-2">Application</th>
                <th className="px-4 py-2">Repository</th>
                {EnvironmentTiers.map((tier) => (
                  <th key={tier} className="px-4 py-2">{tier}</th>
                ))}
                {canManage && <th className="px-4 py-2">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {filtered.map((app) => (
                <ApplicationRow
                  key={app.id}
                  app={app}
                  latest={latest}
                  canManage={canManage}
                  repositories={data.repositories}
                  isEditing={editingId === app.id}
                  onToggleEdit={() => setEditingId((cur) => (cur === app.id ? null : app.id))}
                  onChanged={() => {
                    setEditingId(null);
                    reload();
                  }}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Card className="mt-4 text-xs text-slate-500">
        Branch and latest-available-commit details are shown on each application's page to avoid a live repository
        lookup for every row in this list.
      </Card>
    </div>
  );
}

function ApplicationRow({
  app,
  latest,
  canManage,
  repositories,
  isEditing,
  onToggleEdit,
  onChanged,
}: {
  app: ApplicationDto;
  latest: Map<string, DeploymentDto>;
  canManage: boolean;
  repositories: RepositoryDto[];
  isEditing: boolean;
  onToggleEdit: () => void;
  onChanged: () => void;
}) {
  const colSpan = EnvironmentTiers.length + 2 + (canManage ? 1 : 0);

  return (
    <>
      <tr className={app.isActive ? '' : 'opacity-60'}>
        <td className="px-4 py-2.5">
          <Link to={`/applications/${app.id}`} className="font-medium text-slate-900 hover:underline">
            {app.name}
          </Link>
          {!app.isActive && <span className="ml-2 text-xs text-slate-400">(inactive)</span>}
        </td>
        <td className="px-4 py-2.5 text-slate-600">{app.repositoryName ?? '—'}</td>
        {EnvironmentTiers.map((tier) => {
          const deployment = latest.get(appEnvKey(app.id, tier));
          return (
            <td key={tier} className="px-4 py-2.5">
              {deployment ? (
                <div className="flex flex-col gap-0.5">
                  <DeploymentStatusBadge status={deployment.status} />
                  <span className="text-[11px] text-slate-400">{formatRelative(deployment.requestedAt)}</span>
                </div>
              ) : (
                <span className="text-xs text-slate-400">Not deployed</span>
              )}
            </td>
          );
        })}
        {canManage && (
          <td className="px-4 py-2.5">
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={onToggleEdit} className="text-xs font-medium text-slate-500 hover:text-slate-700">
                {isEditing ? 'Cancel' : 'Edit'}
              </button>
              <ActionButton
                label={app.isActive ? 'Deactivate' : 'Activate'}
                variant={app.isActive ? 'danger' : 'secondary'}
                confirmLabel={app.isActive ? 'Confirm deactivate' : undefined}
                onAction={() =>
                  ApplicationsApi.update(app.id, {
                    name: app.name,
                    description: app.description,
                    deploymentMode: app.deploymentMode,
                    repositoryId: app.repositoryId,
                    sourcePath: app.sourcePath,
                    isActive: !app.isActive,
                  })
                }
                onSuccess={onChanged}
              />
              <ActionButton
                label="Delete"
                variant="danger"
                confirmLabel="Confirm delete"
                onAction={() => ApplicationsApi.delete(app.id)}
                onSuccess={onChanged}
              />
            </div>
          </td>
        )}
      </tr>
      {isEditing && canManage && (
        <tr>
          <td colSpan={colSpan} className="bg-slate-50 px-4 py-3">
            <ApplicationEditForm app={app} repositories={repositories} onSubmitted={onChanged} onCancel={onToggleEdit} />
          </td>
        </tr>
      )}
    </>
  );
}

function ApplicationEditForm({
  app,
  repositories,
  onSubmitted,
  onCancel,
}: {
  app: ApplicationDto;
  repositories: RepositoryDto[];
  onSubmitted: () => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(app.name);
  const [description, setDescription] = useState(app.description ?? '');
  const [deploymentMode, setDeploymentMode] = useState<DeploymentMode>(app.deploymentMode);
  const [repositoryId, setRepositoryId] = useState(app.repositoryId ?? '');
  const [sourcePath, setSourcePath] = useState(app.sourcePath ?? '');

  const canSubmit = name.trim();

  function buildRequest(): UpdateApplicationRequest {
    return {
      name: name.trim(),
      description: description.trim() || null,
      deploymentMode,
      repositoryId: repositoryId || null,
      sourcePath: sourcePath.trim() || null,
      isActive: app.isActive,
    };
  }

  return (
    <div>
      <h3 className="text-xs font-semibold text-slate-900">Edit application</h3>
      <div className="mt-2 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Field label="Name">
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />
        </Field>
        <Field label="Deployment mode">
          <select value={deploymentMode} onChange={(e) => setDeploymentMode(Number(e.target.value) as DeploymentMode)} className={inputClass}>
            {Object.entries(deploymentModeLabels).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </Field>
        <Field label="Repository">
          <select value={repositoryId} onChange={(e) => setRepositoryId(e.target.value)} className={inputClass}>
            <option value="">None</option>
            {repositories.map((repo) => (
              <option key={repo.id} value={repo.id}>{repo.name}</option>
            ))}
          </select>
        </Field>
        <Field label="Source path">
          <input value={sourcePath} onChange={(e) => setSourcePath(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Description" className="sm:col-span-2 lg:col-span-4">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
      </div>
      <div className="mt-3 flex gap-2">
        <ActionButton
          label="Save changes"
          disabled={!canSubmit}
          disabledReason="Name is required."
          onAction={() => ApplicationsApi.update(app.id, buildRequest())}
          onSuccess={onSubmitted}
        />
        <button type="button" onClick={onCancel} className="text-xs font-medium text-slate-500 hover:text-slate-700">
          Cancel
        </button>
      </div>
    </div>
  );
}

function ApplicationCreateForm({ repositories, onSubmitted }: { repositories: RepositoryDto[]; onSubmitted: () => void }) {
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [deploymentMode, setDeploymentMode] = useState<DeploymentMode>(DeploymentMode.LegacyFilesystem);
  const [repositoryId, setRepositoryId] = useState('');
  const [sourcePath, setSourcePath] = useState('');

  const canSubmit = name.trim() && slug.trim();

  function buildRequest(): CreateApplicationRequest {
    return {
      name: name.trim(),
      slug: slug.trim(),
      description: description.trim() || null,
      deploymentMode,
      repositoryId: repositoryId || null,
      sourcePath: sourcePath.trim() || null,
    };
  }

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New application</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Field label="Name">
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="e.g. Reporting Service" />
        </Field>
        <Field label="Slug">
          <input value={slug} onChange={(e) => setSlug(e.target.value)} className={inputClass} placeholder="e.g. reporting-service" />
        </Field>
        <Field label="Deployment mode">
          <select value={deploymentMode} onChange={(e) => setDeploymentMode(Number(e.target.value) as DeploymentMode)} className={inputClass}>
            {Object.entries(deploymentModeLabels).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </Field>
        <Field label="Repository">
          <select value={repositoryId} onChange={(e) => setRepositoryId(e.target.value)} className={inputClass}>
            <option value="">None</option>
            {repositories.map((repo) => (
              <option key={repo.id} value={repo.id}>{repo.name}</option>
            ))}
          </select>
        </Field>
        <Field label="Source path">
          <input value={sourcePath} onChange={(e) => setSourcePath(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
        <Field label="Description" className="sm:col-span-2 lg:col-span-4">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </Field>
      </div>
      <p className="mt-2 text-xs text-slate-400">
        After creating this application, configure it per environment (target server, deployment path, application URL) from its details page.
      </p>
      <div className="mt-3">
        <ActionButton
          label="Create application"
          disabled={!canSubmit}
          disabledReason="Name and slug are required."
          onAction={() => ApplicationsApi.create(buildRequest())}
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
