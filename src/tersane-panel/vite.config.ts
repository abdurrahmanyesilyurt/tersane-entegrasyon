import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the panel proxies /api to the MVC app, so no CORS setup is needed.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': process.env.TERSANE_WEB_URL ?? 'http://localhost:5100' },
  },
});
