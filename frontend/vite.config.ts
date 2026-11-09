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
      '/api': { target: 'http://localhost:5078', changeOrigin: true },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
