import ReactDOM from 'react-dom/client';
import 'react-photo-view/dist/react-photo-view.css';
import { BrowserRouter } from 'react-router-dom';

import App from '@/App.tsx';
import { Provider } from '@/provider.tsx';
import '@/styles/globals.css';

import key from './const/key';
import WebUIManager from './controllers/webui_manager';
import { initFont, loadTheme } from './utils/theme';

WebUIManager.checkWebUiLogined();

const token = localStorage.getItem(key.token);

if (token && !token.startsWith('"')) {
  localStorage.setItem(key.token, JSON.stringify(token));
}

localStorage.setItem('theme', '"dark"');
document.documentElement.classList.remove('light');
document.documentElement.classList.add('dark');

loadTheme();
initFont();

ReactDOM.createRoot(document.getElementById('root')!).render(
  <BrowserRouter basename='/webui/'>
    <Provider>
      <App />
    </Provider>
  </BrowserRouter>
);

if (!import.meta.env.DEV) {
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      const baseUrl = import.meta.env.BASE_URL;
      const swUrl = `${baseUrl}sw.js`;
      navigator.serviceWorker.register(swUrl, { scope: baseUrl })
        .then((registration) => {
          console.log('SW registered: ', registration);
        })
        .catch((registrationError) => {
          console.log('SW registration failed: ', registrationError);
        });
    });
  }
}
