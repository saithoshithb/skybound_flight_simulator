import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Web Serial requires a secure context; http://localhost qualifies, a LAN IP does not.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { host: 'localhost', port: 5173 },
});
