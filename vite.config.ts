import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: { port: 5174 },
  resolve: {
    // Force a single React instance — guards against a second copy being
    // resolved (e.g. when the dev server is launched from a parent workspace).
    dedupe: ['react', 'react-dom'],
    alias: {
      '@game': fileURLToPath(new URL('./src/game', import.meta.url)),
    },
  },
});
