import { Suspense, lazy, useEffect, useState } from 'react';
import { Provider } from 'react-redux';
import { Route, Routes, useLocation, useNavigate } from 'react-router-dom';

import PageBackground from '@/components/page_background';
import PageLoading from '@/components/page_loading';
import Toaster from '@/components/toaster';

import DialogProvider from '@/contexts/dialog';

import useAuth from '@/hooks/auth';

import store from '@/store';

import WebUIManager from '@/controllers/webui_manager';

const WebLoginPage = lazy(() => import('@/pages/web_login'));
const IndexPage = lazy(() => import('@/pages/index'));
const DashboardIndexPage = lazy(() => import('@/pages/dashboard'));
const AboutPage = lazy(() => import('@/pages/dashboard/about'));
const ConfigPage = lazy(() => import('@/pages/dashboard/config'));
const LogsPage = lazy(() => import('@/pages/dashboard/logs'));
const PluginPage = lazy(() => import('@/pages/dashboard/plugin'));
const PluginConfigPage = lazy(() => import('@/pages/dashboard/plugin_config'));

function App () {
  return (
    <DialogProvider>
      <Provider store={store}>
        <PageBackground />
        <Toaster />
        <Suspense fallback={<PageLoading />}>
          <Routes>
            <Route path='/web_login' element={<WebLoginPage />} />
            <Route path='/*' element={<AuthChecker><AppRoutes /></AuthChecker>} />
          </Routes>
        </Suspense>
      </Provider>
    </DialogProvider>
  );
}

function AuthChecker ({ children }: { children: React.ReactNode; }) {
  const { isAuth, revokeAuth } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [isChecked, setIsChecked] = useState(false);
  const [isValidating, setIsValidating] = useState(true);

  useEffect(() => {
    const validateAuth = async () => {
      console.log('[AuthChecker] Validating auth, isAuth:', isAuth, 'path:', location.pathname);
      
      if (!isAuth) {
        console.log('[AuthChecker] No token found, redirecting to login');
        redirectToLogin();
        return;
      }

      try {
        const isValid = await WebUIManager.checkWebUiLogined();
        console.log('[AuthChecker] Token validation result:', isValid);
        
        if (isValid) {
          console.log('[AuthChecker] Auth check passed');
          setIsChecked(true);
        } else {
          console.log('[AuthChecker] Token invalid, redirecting to login');
          revokeAuth();
          redirectToLogin();
        }
      } catch (error) {
        console.error('[AuthChecker] Token validation failed:', error);
        revokeAuth();
        redirectToLogin();
      } finally {
        setIsValidating(false);
      }
    };

    const redirectToLogin = () => {
      const search = new URLSearchParams(window.location.search);
      const token = search.get('token');
      let url = '/web_login';

      if (token) {
        url += `?token=${token}`;
      }
      console.log('[AuthChecker] Redirecting to:', url);
      navigate(url, { replace: true });
    };

    validateAuth();
  }, [isAuth, navigate, location.pathname, revokeAuth]);

  if (isValidating || !isChecked || !isAuth) {
    return <PageLoading />;
  }

  return <>{children}</>;
}

function AppRoutes () {
  return (
    <Routes>
      <Route path='/' element={<IndexPage />}>
        <Route index element={<DashboardIndexPage />} />
        <Route path='logs' element={<LogsPage />} />
        <Route path='plugins' element={<PluginPage />} />
        <Route path='plugin-config' element={<PluginConfigPage />} />
        <Route path='config' element={<ConfigPage />} />
        <Route path='about' element={<AboutPage />} />
      </Route>
    </Routes>
  );
}

export default App;
