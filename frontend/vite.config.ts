// Configuration for Vite, the build tool/dev server powering the frontend
// (similar in spirit to webpack/CRA, but much faster). Running `npm run dev`
// reads this file to know how to serve the app locally.
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()], // enables JSX/Fast Refresh support for React
  server: {
    host: true, // listen on 0.0.0.0, not just localhost - required so the dev server is reachable from outside its Docker container
    port: 5173,
  },
});
