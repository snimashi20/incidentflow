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
    watch: {
      // Why polling instead of the default (and much cheaper) OS file events:
      // the source lives on the Windows host and is bind-mounted into a Linux
      // container (see docker-compose.yml). Linux's inotify notifications do
      // not cross that boundary, so Vite's watcher never fires, and the dev
      // server happily keeps serving the version of each file it cached at
      // startup. The symptom is nasty precisely because nothing looks broken:
      // you edit a file, save it, refresh the browser, and see the OLD page
      // with no error anywhere - the only fix being to restart the container.
      // Polling re-checks the files on a timer instead, which costs a little
      // CPU but makes hot reload actually work.
      usePolling: true,
      interval: 300,
    },
  },
});
