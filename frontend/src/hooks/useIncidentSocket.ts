import { useEffect, useRef } from "react";
import { wsUrl } from "../api/client";
import type { WsEvent } from "../types";

const RECONNECT_DELAY_MS = 2000;

/**
 * Opens a WebSocket for one incident's room and calls onEvent for every
 * broadcast the server sends. Reconnects automatically if the connection
 * drops (e.g. backend restart) so a stale tab keeps receiving updates.
 *
 * `onReconnect` is called after a *re*connection, never after the first one,
 * and it matters more than it looks. These events are fire-and-forget: the
 * server publishes them to whoever happens to be listening and keeps no
 * record for anyone who wasn't. So every comment posted, task added and
 * status changed while a tab was disconnected is simply lost to that tab -
 * reconnecting restores the live feed but not the gap, which left the page
 * silently, permanently wrong. Re-fetching the incident on reconnect is what
 * closes that gap: the socket handles what happens next, the fetch handles
 * what was missed.
 *
 * (The thorough fix is replayable events - Redis Streams with a "resume from
 * event id" cursor, rather than pub/sub. This is the cheap version that
 * removes the silent-staleness problem without that machinery.)
 */
export function useIncidentSocket(
  incidentId: number,
  onEvent: (event: WsEvent) => void,
  onReconnect?: () => void,
) {
  // Both callbacks are kept in refs so the effect below doesn't need them in
  // its dependency array. Without this, every render would make a new
  // function, which would tear down and rebuild the socket each time.
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

  const onReconnectRef = useRef(onReconnect);
  onReconnectRef.current = onReconnect;

  useEffect(() => {
    let socket: WebSocket | null = null;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let closedByCleanup = false;
    // Distinguishes "connected" from "connected again". The page fetches the
    // incident itself on mount, so refetching on the very first connection
    // would just be the same request twice.
    let hasConnectedBefore = false;

    function scheduleReconnect() {
      if (!closedByCleanup) {
        reconnectTimer = setTimeout(connect, RECONNECT_DELAY_MS);
      }
    }

    async function connect() {
      try {
        // Opening a socket now takes an HTTP round trip first, to swap the
        // access token for a short-lived ticket (see wsUrl).
        const url = await wsUrl(`/ws/incidents/${incidentId}`);

        // The component may have been unmounted while that request was in
        // flight - without this check we would open a socket nobody is
        // listening to and leak it.
        if (closedByCleanup) return;

        socket = new WebSocket(url);

        socket.onopen = () => {
          if (hasConnectedBefore) {
            onReconnectRef.current?.();
          }
          hasConnectedBefore = true;
        };

        socket.onmessage = (event) => {
          const parsed = JSON.parse(event.data) as WsEvent;
          onEventRef.current(parsed);
        };

        socket.onclose = scheduleReconnect;
      } catch {
        // Getting the ticket failed - the backend is down, or our token has
        // expired. Either way, keep trying on the same schedule as a dropped
        // socket rather than giving up silently.
        scheduleReconnect();
      }
    }

    connect();

    return () => {
      closedByCleanup = true;
      if (reconnectTimer) clearTimeout(reconnectTimer);
      socket?.close();
    };
  }, [incidentId]); // re-run only when we're now looking at a different incident
}
