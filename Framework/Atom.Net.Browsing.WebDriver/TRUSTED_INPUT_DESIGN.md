# Page-Local Input Design

## Status

Atom WebDriver intentionally runs in page-local only mode.

Trusted native input was explored and then removed from the active runtime path and public preference surface. The remaining design is therefore centered on synthetic, page-local input only.

## Problem

Page-local input has a hard browser trust boundary:

- DOM events created from JavaScript stay `isTrusted = false`
- `navigator.userActivation` does not become active from synthetic clicks or keys
- anti-bot widgets may still reject gestures even when target discovery and iframe routing are correct

The Turnstile investigation confirmed this boundary directly: targeting can be correct while trust state remains unchanged.

## Decision

Atom does not pursue trusted native input as a mainline WebDriver feature.

The trusted-input branch was dropped for practical reasons:

- generic Wayland support without platform-specific privilege models is not realistic
- xdg-desktop-portal RemoteDesktop requires explicit user consent, which is incompatible with a silent automation path
- non-portal Wayland paths are compositor-specific, privileged, unstable, or all three
- system-wide injection paths like `/dev/uinput` do not fit the project architecture as a default design
- the implementation and maintenance cost is disproportionate to the value of a single more trusted click path

## Goals

- Keep the default production path CDP-free
- Preserve tab-local target discovery and interaction
- Preserve headless-safe behaviour
- Preserve parallel-safe execution across tabs
- Keep the implementation small, predictable, and platform-agnostic at runtime

## Non-Goals

- No attempt to fake `isTrusted` in JavaScript
- No return to `browser.debugger` or CDP input emulation
- No platform-specific native input backend as a supported production path
- No hidden global cursor or machine-exclusive input manager

## Active Architecture

Input remains page-local.

The browser-side code is responsible for:

- finding the correct frame
- resolving the final DOM target
- computing viewport coordinates inside that tab
- dispatching synthetic pointer and keyboard interactions inside the page context
- collecting diagnostics when a site still rejects the interaction

This architecture stays aligned with the project constraints:

- tab-local
- headless-safe
- parallel-safe
- free of compositor-specific runtime dependencies

## Input Preference Policy

The supported page input preferences are now:

- `Default`
  - use the standard page-local backend
- `PreferParallel`
  - explicitly prefer the same page-local backend model when the caller wants to state that requirement

There is no longer a trusted preference in the public API.

## Why Trusted Input Was Rejected

The trusted-input investigation still produced useful conclusions.

### Wayland portal path

The RemoteDesktop portal can bootstrap pointer and keyboard control and even yield an EIS file descriptor, but it is not suitable for a silent automation feature because the session start flow normally presents a system consent dialog.

### Non-portal Wayland paths

The remaining alternatives are compositor-specific and privileged:

- wlroots virtual pointer and virtual keyboard protocols
- KWin fake input
- compositor-specific EIS exposure

Those paths are not a generic, stable, low-maintenance basis for Atom WebDriver.

### X11 and system-wide injection

Even where native injection is technically possible, it introduces focus, visibility, serialization, and machine-scope tradeoffs that do not match the project’s mainline architecture.

## Parallelism Strategy

Parallelism remains simple:

- each tab keeps its own DOM discovery and page execution path
- no global native-input lock exists
- no window-scoped trusted scheduling exists
- no compositor or desktop-session privilege step exists at runtime

## Operational Guidance

If a site requires genuinely trusted native input, that requirement is currently outside the supported WebDriver model.

The correct response is not to silently fall back to platform-specific hacks. Instead:

- continue using page-local input where it is sufficient
- treat trust-boundary failures as a real limitation of the automation model
- investigate site-specific diagnostics or alternate workflows at a higher product level, not by expanding runtime platform complexity inside WebDriver

## Why This Design Fits Atom

This design keeps the strengths that actually scale in Atom WebDriver:

- CDP-free production control plane
- tab-local DOM execution
- good parallelism across tabs
- headless-safe behaviour
- low platform coupling

It also keeps the product honest: when a site requires OS-level trust, that is treated as a boundary of the current system rather than hidden behind an expensive and fragile backend matrix.
