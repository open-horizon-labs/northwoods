import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Render's SPA fallback intercepts requests with an Origin header and returns
// index.html instead of the static asset. Vite adds crossorigin to stylesheet
// links which forces the browser to send Origin, breaking CSS on Render.
// This plugin strips crossorigin from stylesheet links (assets are same-origin).
const removeCssLinkCrossorigin = {
  name: 'remove-css-link-crossorigin',
  transformIndexHtml(html: string) {
    return html.replace(
      /<link rel="stylesheet" crossorigin/g,
      '<link rel="stylesheet"'
    )
  },
}

export default defineConfig({
  plugins: [react(), tailwindcss(), removeCssLinkCrossorigin],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5100',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
