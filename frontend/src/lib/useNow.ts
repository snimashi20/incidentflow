// A clock you can render.
//
// React re-renders when state changes, and nothing changes when a minute
// passes - so a countdown written as plain `new Date()` inside a component
// would be drawn once and then sit there, frozen, slowly becoming a lie.
// This hook fixes that by making the current time a piece of state that
// updates on a timer.
import { useEffect, useState } from "react";

export function useNow(intervalMs = 15000): Date {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);

    // Clearing the interval when the component goes away matters: without
    // it, every incident page you ever opened would keep its timer running
    // for the life of the tab, each one trying to update a component that
    // no longer exists.
    return () => clearInterval(timer);
  }, [intervalMs]);

  return now;
}
