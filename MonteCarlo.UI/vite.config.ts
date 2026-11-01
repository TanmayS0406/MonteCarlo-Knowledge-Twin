import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The .NET API runs on http://localhost:5080; /api calls are proxied to it.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
})
