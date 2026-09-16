// The actual entry point of the frontend - this is the first file that runs
// in the browser (loaded from index.html). Its only job is to mount the
// <App /> component into the page's #root element.
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  // StrictMode is a development-only helper that intentionally double-invokes
  // some functions to help catch bugs (like effects with missing cleanup) -
  // it has no effect on the production build.
  <StrictMode>
    {/* BrowserRouter enables all the <Route>/<Link>/useNavigate calls used
        throughout the app, by hooking into the browser's URL bar. */}
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
