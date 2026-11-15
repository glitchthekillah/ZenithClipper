# Editor rebuild direction

Approved: use MLT as the media engine; ZenithClipper provides its own interface. Support Windows and Linux. Keep the capture daemon independent and lightweight. Retain charcoal/ice-blue branding and progressive disclosure of advanced tools.

Scope: target the capability classes in Adobe's Premiere desktop guide except Firefly/generative media, Adobe Stock and Frame.io. Core editing must work locally. Adobe-only application interoperability and proprietary formats need independently implemented equivalents or separately verified integration support; MLT does not automatically provide all Premiere features.

Architecture: frame-based saved projects and reversible edit commands feed an MLT composition graph. A separately launched editor loads the native MLT runtime on demand for audio/video preview and export. Windows recording engine does not load editor dependencies. Maintain an explicit implemented/tested/pending capability ledger.

Immediate delivery sequence: runtime validation; project/sequence/track/clip model and undo history; multi-clip editing and layered MLT render tests; editor timeline/project/media tools; playback with audio and export; then effects/keyframes/graphics/captions/color/audio/multicam/proxies/interchange/VR and advanced tooling in tracked increments. Do not describe the initial foundation as full Premiere parity.

Sources: https://helpx.adobe.com/premiere/desktop.html and https://www.mltframework.org/docs/framework/ . Earlier standalone HTTP scraping of Adobe's site was blocked; the reference index is incomplete and must not be presented as an exhaustive audited list yet.
