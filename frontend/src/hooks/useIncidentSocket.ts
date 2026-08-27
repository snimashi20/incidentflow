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
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

  useEffect(() => {
    let socket: WebSocket | null = null;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let closedByCleanup = false;

    function connect() {
      socket = new WebSocket(wsUrl(`/ws/incidents/${incidentId}`));

      socket.onmessage = (event) => {
        const parsed = JSON.parse(event.data) as WsEvent;
        onEventRef.current(parsed);
      };

      socket.onclose = () => {
        if (!closedByCleanup) {
          reconnectTimer = setTimeout(connect, RECONNECT_DELAY_MS);
        }
      };
    }

    connect();

    return () => {
      closedByCleanup = true;
      if (reconnectTimer) clearTimeout(reconnectTimer);
      socket?.close();
    };
  }, [incidentId]);
}
