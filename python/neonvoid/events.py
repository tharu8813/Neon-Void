"""Optional game event hooks."""

from __future__ import annotations



from dataclasses import dataclass
from typing import Any, Callable
import logging

@dataclass(frozen=True)
class GameEvent:
    name: str
    payload: dict[str, Any]

EventHandler = Callable[[GameEvent], None]

class EventBus:
    """Simple single-thread gameplay extension hooks."""
    def __init__(self):
        self._subscribers: dict[str, list[EventHandler]] = {}

    def subscribe(self, name: str, handler: EventHandler) -> None:
        if not name or not callable(handler):
            raise ValueError("A name and callable handler are required")
        subscribers = self._subscribers.setdefault(name, [])
        if handler not in subscribers:
            subscribers.append(handler)

    def unsubscribe(self, name: str, handler: EventHandler) -> None:
        subscribers = self._subscribers.get(name)
        if subscribers and handler in subscribers:
            subscribers.remove(handler)
            if not subscribers:
                del self._subscribers[name]

    def emit(self, name: str, **payload: Any) -> GameEvent:
        event = GameEvent(name, payload)
        for handler in tuple(self._subscribers.get(name, ())):
            try:
                handler(event)
            except Exception:
                logging.getLogger(__name__).exception("Event handler failed: %s", name)
        return event
