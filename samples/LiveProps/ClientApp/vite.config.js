import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Builds to ../wwwroot/build/app.js, the file the Razor root view loads.
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../wwwroot/build',
    emptyOutDir: true,
    rollupOptions: {
      input: 'src/app.js',
      output: {
        entryFileNames: 'app.js',
        chunkFileNames: '[name].js',
        assetFileNames: '[name][extname]',
      },
    },
  },
})
