// This is the frontend half of the real-time feature (the backend half is
// ws_manager.py). A "custom hook" in React is just a function starting with
// "use" that can call other hooks (useEffect, useRef, ...) - it lets us
// package up "manage a WebSocket connection" as one reusable piece of logic.
import { useEffect, useRef } from "react";
import { wsUrl } from "../api/client";
import type { WsEvent } from "../types";

const RECONNECT_DELAY_MS = 2000;

/**
 * Opens a WebSocket for one incident's room and calls onEvent for every
 * broadcast the server sends. Reconnects automatically if the connection
 * drops (e.g. backend restart) so a stale tab keeps receiving updates.
 */
export function useIncidentSocket(incidentId: number, onEvent: (event: WsEvent) => void) {
  // Why a ref instead of just using `onEvent` directly inside the effect
  // below? Because IncidentDetailPage passes a brand new `onEvent` function
  // on every render, and we don't want that to close and reopen the
  // WebSocket each time - only when `incidentId` actually changes (see the
  // dependency array on useEffect below). Storing the latest callback in a
  // ref lets the effect always call the newest version without needing to
  // re-run when it changes.
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

  useEffect(() => {
    let socket: WebSocket | null = null;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let closedByCleanup = false;

    function connect() {
      socket = new WebSocket(wsUrl(`/ws/incidents/${incidentId}`));

      socket.onmessage = (event) => {
        // Every message from the server is a JSON string; parse it back into
        // the WsEvent shape defined in types/index.ts.
        const parsed = JSON.parse(event.data) as WsEvent;
        onEventRef.current(parsed);
      };

      socket.onclose = () => {
        // Only auto-reconnect if the socket closed unexpectedly (e.g. the
        // backend restarted) - not if our own cleanup function closed it on
        // purpose (e.g. because the user navigated to a different incident).
        if (!closedByCleanup) {
          reconnectTimer = setTimeout(connect, RECONNECT_DELAY_MS);
        }
      };
    }

    connect();

    // React calls this cleanup function when the component unmounts, or
    // before re-running the effect (e.g. incidentId changed) - it closes the
    // old connection so we never end up with two sockets open at once.
    return () => {
      closedByCleanup = true;
      if (reconnectTimer) clearTimeout(reconnectTimer);
      socket?.close();
    };
  }, [incidentId]); // re-run only when we're now looking at a different incident
}
