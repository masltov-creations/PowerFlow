# PowerFlow Operating Rules

## reference-host UI Safety — HARD RULE

reference-host is an actively used desktop and gaming machine. PowerFlow development MUST NOT create visible desktop interference on reference-host unless the user explicitly authorizes that specific live-UI action in the current conversation.

Without that explicit authorization, DO NOT:
- launch PowerFlow in any mode that creates or may create a visible window;
- use `--preview`, `--popup-preview`, dashboard/fullscreen preview, or equivalent visible launch paths;
- click or hover the tray icon, invoke notification-area UI, or manipulate tray geometry;
- use UI Automation, focus/activation APIs, `SetForegroundWindow`, synthetic input, or any mechanism that may steal focus;
- open, move, resize, capture, flash, or surface a PowerFlow window;
- start any helper process that displays a console, prompt, dialog, toast, or other visible UI.

Default execution on reference-host is HEADLESS ONLY: source/document edits, tests, builds, static inspection, background-safe telemetry/log inspection, and other operations proven not to create visible UI.

A prior approval for a different UI action does not carry forward. Live-UI validation requires a fresh explicit authorization for that action. If validation requires UI and no authorization exists, stop at the headless gate and report what remains.
