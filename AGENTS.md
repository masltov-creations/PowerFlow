# PowerFlow Operating Rules

## Active-desktop UI safety

PowerFlow is commonly developed and validated on actively used Windows desktops. Development MUST NOT create visible desktop interference unless the user explicitly authorizes that live-UI action in the current conversation.

Without that explicit authorization, do not:

- launch PowerFlow in a mode that creates or may create a visible window;
- use preview, popup-preview, dashboard, or fullscreen launch paths;
- click or hover the tray icon or manipulate notification-area UI;
- use UI Automation, focus/activation APIs, synthetic input, or cursor movement;
- open, move, resize, capture, flash, or otherwise surface a PowerFlow window;
- start helper processes that display a console, prompt, dialog, toast, or other visible UI.

Default execution is headless: source/document edits, tests, builds, static inspection, background-safe telemetry/log inspection, and operations proven not to create visible UI.

A prior approval for a different UI action does not carry forward. If validation requires visible UI and no authorization exists, stop at the headless gate and report what remains.

## Repository hygiene

- Do not commit generated `bin`, `obj`, test-result, publish, release, or IDE-state directories.
- Do not commit credentials, tokens, private keys, local configuration, machine-specific absolute paths, or per-user deployment state.
- Keep runtime/deployment state under `%LOCALAPPDATA%\PowerFlow`, outside the repository.
- Historical documents may describe superseded experiments; current behavior is defined by `README.md`, `docs/product.md`, and `docs/architecture.md`.