/// <reference types="vitest/config" />
import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  build: {
    // Vite inlines small files as data: URIs. Fonts must stay real files: our Content-Security-Policy
    // allows fonts only from our own origin ("font-src 'self'"), and that's stricter than allowing data:.
    assetsInlineLimit: (filePath) => (/\.(woff2?|ttf|otf)$/.test(filePath) ? false : undefined),
  },
  server: {
    port: 5173,
    strictPort: true,
    // The browser only ever talks to http://localhost:5173. Vite forwards /api to the ASP.NET API.
    // Same origin = the SameSite=Strict refresh cookie just works, exactly like production,
    // where a Cloudflare Pages Function plays this proxy role.
    proxy: {
      // API_PROXY_TARGET lets you point the dev server at another API instance (e.g. a second port).
      '/api': { target: process.env.API_PROXY_TARGET ?? 'http://localhost:5078', changeOrigin: true },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Form tests type character by character through user-event. With all files in parallel on a busy machine a few
    // of them need more than the 5s default; 15s still catches a genuinely hung test.
    testTimeout: 15_000,
  },
})
