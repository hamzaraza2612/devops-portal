import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApplicationsApi, DeploymentsApi, RepositoriesApi } from '../api/endpoints';
import { ActionButton } from '../components/ActionButton';
import { Can, Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { DeploymentStatusBadge } from '../components/StatusBadge';
import { useAsyncData } from '../hooks/useAsyncData';
import { useAuth } from '../auth/AuthContext';
import { EnvironmentTiers, Permissions } from '../auth/permissions';
import {
  DeploymentMode,
  type ApplicationDto,
  type CreateApplicationRequest,
  type DeploymentDto,
  type DiscoveredRepositoryFolderDto,
  type RepositoryDto,
  type UpdateApplicationRequest,
} from '../types/api';
import { appEnvKey, latestByAppEnvironment } from '../utils/deploymentIndex';
import { formatRelative } from '../utils/format';

/** Folder path -> a reasonable default application name/slug, so "Discover
 * applications from repository" never requires typing a source folder path
 * by hand — only confirming the pre-filled Create form. */
function slugify(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}
function titleCaseFromFolder(path: string) {
  const last = path.split('/').pop() ?? path;
  return last.replace(/[-_]+/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

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
  const [showDiscover, setShowDiscover] = useState(false);
  const [createInitial, setCreateInitial] = useState<{ name: string; slug: string; repositoryId: string; sourcePath: string } | null>(null);
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
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => {
                  setShowDiscover((v) => !v);
                  setShowCreate(false);
                }}
                className="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
              >
                {showDiscover ? 'Cancel' : 'Discover from repository'}
              </button>
              <button
                type="button"
                onClick={() => {
                  setCreateInitial(null);
                  setShowCreate((v) => !v);
                  setShowDiscover(false);
                }}
                className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
              >
                {showCreate ? 'Cancel' : 'New Application'}
              </button>
            </div>
          </Can>
        }
      />

      {showDiscover && (
        <DiscoverApplicationsPanel
          repositories={data.repositories}
          onCreateFromFolder={(repo, folder) => {
            setCreateInitial({
              name: titleCaseFromFolder(folder.path),
              slug: slugify(folder.path),
              repositoryId: repo.id,
              sourcePath: folder.path,
            });
            setShowDiscover(false);
            setShowCreate(true);
          }}
        />
      )}

      {showCreate && (
        <ApplicationCreateForm
          key={createInitial ? `discovered-${createInitial.sourcePath}` : 'blank'}
          repositories={data.repositories}
          initial={createInitial}
          onSubmitted={() => {
            setShowCreate(false);
            setCreateInitial(null);
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

/** "Discover applications from this repository" — scans a monorepo-style
 * repository (one folder per app, e.g. the Techbey techbey-apps/
 * techbey-apps8 transition) and lists its top-level folders, so the source
 * folder never has to be typed in by hand — just confirmed on the Create form. */
function DiscoverApplicationsPanel({
  repositories,
  onCreateFromFolder,
}: {
  repositories: RepositoryDto[];
  onCreateFromFolder: (repo: RepositoryDto, folder: DiscoveredRepositoryFolderDto) => void;
}) {
  const [repositoryId, setRepositoryId] = useState(repositories[0]?.id ?? '');
  const [branch, setBranch] = useState('');
  const [scanning, setScanning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [folders, setFolders] = useState<DiscoveredRepositoryFolderDto[] | null>(null);
  const [scannedBranch, setScannedBranch] = useState<string | null>(null);

  const repo = repositories.find((r) => r.id === repositoryId);

  async function scan() {
    if (!repo) return;
    setScanning(true);
    setError(null);
    try {
      const result = await RepositoriesApi.discoverApplications(repo.id, branch.trim() || undefined);
      if (!result.success) {
        setError(result.errorMessage ?? 'Failed to scan the repository.');
        setFolders(null);
      } else {
        setFolders(result.folders);
        setScannedBranch(result.branch);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to scan the repository.');
      setFolders(null);
    } finally {
      setScanning(false);
    }
  }

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">Discover applications from repository</h2>
      {repositories.length === 0 ? (
        <p className="mt-2 text-xs text-slate-400">
          No repositories configured yet — add one on the Repositories admin page first.
        </p>
      ) : (
        <>
          <div className="mt-3 grid gap-3 sm:grid-cols-3">
            <Field label="Repository">
              <select value={repositoryId} onChange={(e) => { setRepositoryId(e.target.value); setFolders(null); }} className={inputClass}>
                {repositories.map((r) => (
                  <option key={r.id} value={r.id}>{r.name}</option>
                ))}
              </select>
            </Field>
            <Field label="Branch">
              <input
                value={branch}
                onChange={(e) => setBranch(e.target.value)}
                className={inputClass}
                placeholder={repo?.defaultBranch ?? 'main'}
              />
            </Field>
            <div className="flex items-end">
              <button
                type="button"
                onClick={scan}
                disabled={scanning || !repo}
                className="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
              >
                {scanning ? 'Scanning…' : 'Scan'}
              </button>
            </div>
          </div>

          {error && <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-xs font-medium text-red-700">{error}</p>}

          {folders && (
            <div className="mt-3">
              <p className="mb-2 text-xs text-slate-500">
                {folders.length} folder(s) found at '{scannedBranch}'.
              </p>
              {folders.length === 0 ? (
                <EmptyState title="No folders found at this branch." />
              ) : (
                <ul className="divide-y divide-slate-100 rounded-md border border-slate-200">
                  {folders.map((folder) => (
                    <li key={folder.path} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                      <div>
                        <span className="font-mono text-slate-800">{folder.path}</span>
                        {!folder.hasComposeFile && (
                          <span className="ml-2 text-xs text-slate-400">(no compose file found)</span>
                        )}
                      </div>
                      {folder.existingApplicationId ? (
                        <Link to={`/applications/${folder.existingApplicationId}`} className="text-xs font-medium text-slate-500 hover:underline">
                          Already linked to '{folder.existingApplicationName}'
                        </Link>
                      ) : folder.hasComposeFile ? (
                        <button
                          type="button"
                          onClick={() => repo && onCreateFromFolder(repo, folder)}
                          className="text-xs font-medium text-blue-600 hover:underline"
                        >
                          Create application
                        </button>
                      ) : (
                        <span className="text-xs text-slate-300">Not deployable</span>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </>
      )}
    </Card>
  );
}

function ApplicationCreateForm({
  repositories,
  initial,
  onSubmitted,
}: {
  repositories: RepositoryDto[];
  initial?: { name: string; slug: string; repositoryId: string; sourcePath: string } | null;
  onSubmitted: () => void;
}) {
  const [name, setName] = useState(initial?.name ?? '');
  const [slug, setSlug] = useState(initial?.slug ?? '');
  const [description, setDescription] = useState('');
  const [deploymentMode, setDeploymentMode] = useState<DeploymentMode>(DeploymentMode.LegacyFilesystem);
  const [repositoryId, setRepositoryId] = useState(initial?.repositoryId ?? '');
  const [sourcePath, setSourcePath] = useState(initial?.sourcePath ?? '');

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
        {initial
          ? `Pre-filled from '${initial.sourcePath}' in the repository — adjust if needed, then create.`
          : 'After creating this application, configure it per environment (target server, deployment path, application URL) from its details page.'}
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
