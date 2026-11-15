# Engine client protocol

No TCP server. User-scoped .NET named pipe/Unix socket via `NamedPipeServerStream`, name from `Control.PipeName` (hash of absolute data directory). Server/client use `PipeOptions.CurrentUserOnly`. Maximum message: 65,536 characters, one UTF-8 JSON object per line. Maximum eight connected clients; incomplete requests time out after five seconds. State mutations are serialized; one save at a time.

Examples:

```json
{"Command":"status"}
{"Command":"save","Seconds":30}
{"Command":"pause"}
{"Command":"resume"}
{"Command":"settings"}
{"Command":"configure","Settings":{"ClipSeconds":30,"BufferSeconds":120,"MaxBufferMiB":2048,"ExcludedApps":["Spotify"]}}
{"Command":"shutdown"}
```

`configure` accepts a full `Settings` record (omitted fields take defaults); validate before sending, and first read settings to preserve fields. A successful configure persists atomically and restarts active capture with a fresh buffer. A valid saved configuration can still fail hardware initialization; the response then has `Ok=false` and capture error information.

Responses use `ControlResponse`: `Ok`, `Error`, `Status`, `Settings`, `Path`. Errors do not mean a clip was saved. Check `Ok` before displaying success. Status includes Capturing, State, Error, Encoder, BufferedSeconds, BufferBytes, LastClip and ProcessId.

Clients can reference `ZenithClipper.Core` directly and call `Control.SendAsync`. Launch `zenithclipper daemon` only if status cannot connect. Closing UI must terminate only that UI process. `shutdown` explicitly stops the engine.

Edits are independent of the engine: `Editor.ExportAsync(Settings, EditRequest, CancellationToken)` or `zenithclipper export edit-request.json`. A future client may spawn one export worker per explicit operation; do not keep the editor/web runtime loaded in the engine.
