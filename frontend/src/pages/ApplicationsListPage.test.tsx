import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApplicationsListPage } from './ApplicationsListPage';
import { ApplicationsApi, DeploymentsApi, RepositoriesApi } from '../api/endpoints';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import { DeploymentMode, RepositoryProvider, type RepositoryDto } from '../types/api';

vi.mock('../api/endpoints', () => ({
  ApplicationsApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  DeploymentsApi: { list: vi.fn() },
  RepositoriesApi: { list: vi.fn(), discoverApplications: vi.fn() },
}));

function authValue(permissions: string[]): AuthContextValue {
  return {
    user: {
      id: '1', username: 'u', email: 'u@example.local', fullName: 'U', isActive: true, isAdmin: false,
      canApproveProduction: false, environmentAccess: [], createdAt: '', lastLoginAt: null, roles: [], permissions,
    },
    isLoading: false,
    error: null,
    login: async () => {},
    logout: () => {},
    can: (p: string) => permissions.includes(p),
  };
}

function renderPage(permissions: string[]) {
  return render(
    <MemoryRouter>
      <AuthContext.Provider value={authValue(permissions)}>
        <ApplicationsListPage />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const repository: RepositoryDto = {
  id: 'repo-1', name: 'monorepo', url: 'https://gitlab.example.com/group/monorepo.git', provider: RepositoryProvider.GitLab,
  description: null, defaultBranch: 'main', username: null, hasAccessToken: true, accessTokenEnvVarName: null,
  isActive: true, createdAt: '2026-01-01T00:00:00Z',
};

describe('ApplicationsListPage — Discover applications from repository', () => {
  beforeEach(() => {
    vi.mocked(ApplicationsApi.list).mockResolvedValue([]);
    vi.mocked(DeploymentsApi.list).mockResolvedValue([]);
    vi.mocked(RepositoriesApi.list).mockResolvedValue([repository]);
  });

  it('scans a repository and pre-fills the Create form from a discovered folder', async () => {
    vi.mocked(RepositoriesApi.discoverApplications).mockResolvedValue({
      success: true,
      branch: 'main',
      errorMessage: null,
      folders: [
        { path: 'dmsapi', hasComposeFile: true, existingApplicationId: null, existingApplicationName: null },
        { path: 'hrms', hasComposeFile: true, existingApplicationId: 'app-existing', existingApplicationName: 'HRMS' },
        { path: 'docs', hasComposeFile: false, existingApplicationId: null, existingApplicationName: null },
      ],
    });
    const user = userEvent.setup();
    renderPage([Permissions.ApplicationsManage]);

    await user.click(await screen.findByRole('button', { name: 'Discover from repository' }));
    await user.click(screen.getByRole('button', { name: 'Scan' }));

    expect(await screen.findByText('dmsapi')).toBeInTheDocument();
    expect(screen.getByText(/Already linked to 'HRMS'/)).toBeInTheDocument();
    expect(screen.getByText('docs')).toBeInTheDocument();
    expect(screen.getByText(/no compose file found/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create application' }));

    expect(screen.getByLabelText('Name')).toHaveValue('Dmsapi');
    expect(screen.getByLabelText('Slug')).toHaveValue('dmsapi');
    expect(screen.getByLabelText('Source path')).toHaveValue('dmsapi');
    expect(screen.getByText(/Pre-filled from 'dmsapi'/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create application' }));
    expect(ApplicationsApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Dmsapi', slug: 'dmsapi', repositoryId: 'repo-1', sourcePath: 'dmsapi', deploymentMode: DeploymentMode.LegacyFilesystem }),
    );
  });

  it('shows a clear error when the scan fails, never a silent empty result', async () => {
    vi.mocked(RepositoriesApi.discoverApplications).mockResolvedValue({
      success: false, branch: 'main', folders: [], errorMessage: 'GitLab returned 401 Unauthorized.',
    });
    const user = userEvent.setup();
    renderPage([Permissions.ApplicationsManage]);

    await user.click(await screen.findByRole('button', { name: 'Discover from repository' }));
    await user.click(screen.getByRole('button', { name: 'Scan' }));

    expect(await screen.findByText('GitLab returned 401 Unauthorized.')).toBeInTheDocument();
  });
});
