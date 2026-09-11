import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { ErrorBoundary } from './components/ErrorBoundary';
import { Layout } from './components/Layout';
import { RequireAuth, RequirePermission } from './components/RouteGuards';
import { Permissions } from './auth/permissions';
import { ApplicationDetailsPage } from './pages/ApplicationDetailsPage';
import { ApplicationsListPage } from './pages/ApplicationsListPage';
import { CredentialsPage } from './pages/CredentialsPage';
import { DashboardPage } from './pages/DashboardPage';
import { DeploymentDetailsPage } from './pages/DeploymentDetailsPage';
import { DeploymentHistoryPage } from './pages/DeploymentHistoryPage';
import { EnvironmentDashboardPage } from './pages/EnvironmentDashboardPage';
import { EnvironmentsPage } from './pages/EnvironmentsPage';
import { LoginPage } from './pages/LoginPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { PendingRequestsPage } from './pages/PendingRequestsPage';
import { BuildServersPage } from './pages/admin/BuildServersPage';
import { EnvironmentAdminPage } from './pages/admin/EnvironmentsPage';
import { RepositoriesPage } from './pages/admin/RepositoriesPage';
import { TargetServersPage } from './pages/admin/TargetServersPage';
import { UsersPage } from './pages/admin/UsersPage';

export default function App() {
  return (
    <ErrorBoundary>
      <BrowserRouter>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route
              element={
                <RequireAuth>
                  <Layout />
                </RequireAuth>
              }
            >
              <Route index element={<DashboardPage />} />
              <Route
                path="applications"
                element={
                  <RequirePermission permission={Permissions.ApplicationsView}>
                    <ApplicationsListPage />
                  </RequirePermission>
                }
              />
              <Route
                path="applications/:id"
                element={
                  <RequirePermission permission={Permissions.ApplicationsView}>
                    <ApplicationDetailsPage />
                  </RequirePermission>
                }
              />
              <Route
                path="environments"
                element={
                  <RequirePermission permission={Permissions.EnvironmentsView}>
                    <EnvironmentsPage />
                  </RequirePermission>
                }
              />
              <Route
                path="environments/:tier"
                element={
                  <RequirePermission permission={Permissions.EnvironmentsView}>
                    <EnvironmentDashboardPage />
                  </RequirePermission>
                }
              />
              <Route
                path="pending"
                element={
                  <RequirePermission permission={Permissions.DeploymentsView}>
                    <PendingRequestsPage />
                  </RequirePermission>
                }
              />
              <Route
                path="credentials"
                element={
                  <RequirePermission permission={Permissions.SecretsView}>
                    <CredentialsPage />
                  </RequirePermission>
                }
              />
              <Route
                path="deployments"
                element={
                  <RequirePermission permission={Permissions.DeploymentsView}>
                    <DeploymentHistoryPage />
                  </RequirePermission>
                }
              />
              <Route
                path="deployments/:id"
                element={
                  <RequirePermission permission={Permissions.DeploymentsView}>
                    <DeploymentDetailsPage />
                  </RequirePermission>
                }
              />
              <Route
                path="admin/users"
                element={
                  <RequirePermission permission={Permissions.UsersView}>
                    <UsersPage />
                  </RequirePermission>
                }
              />
              <Route
                path="admin/repositories"
                element={
                  <RequirePermission permission={Permissions.RepositoriesView}>
                    <RepositoriesPage />
                  </RequirePermission>
                }
              />
              <Route
                path="admin/target-servers"
                element={
                  <RequirePermission permission={Permissions.TargetServersView}>
                    <TargetServersPage />
                  </RequirePermission>
                }
              />
              <Route
                path="admin/build-servers"
                element={
                  <RequirePermission permission={Permissions.BuildServersView}>
                    <BuildServersPage />
                  </RequirePermission>
                }
              />
              <Route
                path="admin/environments"
                element={
                  <RequirePermission permission={Permissions.EnvironmentsView}>
                    <EnvironmentAdminPage />
                  </RequirePermission>
                }
              />
              <Route path="404" element={<NotFoundPage />} />
              <Route path="*" element={<Navigate to="/404" replace />} />
            </Route>
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </ErrorBoundary>
  );
}
