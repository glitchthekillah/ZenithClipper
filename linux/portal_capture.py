#!/usr/bin/env python3
"""Wayland portal consent + PipeWire/GStreamer H.264 to stdout, only during capture.
Dependencies: python3-gi, GStreamer GI, pipewire/good/bad/va/nvcodec plugins,
xdg-desktop-portal and the compositor's portal backend. No capture bypass.
"""
import os
import signal
import sys
import uuid

# Stop screen sharing if the engine disappears, including before portal consent. This watches the engine *process*
# (PR_SET_PDEATHSIG follows the starting thread, and .NET retires idle threads, which stopped capture after a minute).
parent = os.getppid()
if parent == 1:
    sys.exit(1)

import gi
gi.require_version("Gst", "1.0")
from gi.repository import Gio, GLib, Gst

Gst.init(None)
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
DEST = "org.freedesktop.portal.Desktop"
PATH = "/org/freedesktop/portal/desktop"
IFACE = "org.freedesktop.portal.ScreenCast"
session = None
pipeline = None
remote_fd = None


def request(method, signature, values, options):
    """Subscribe before calling; portal responses can precede the method reply."""
    token = "zenithclipper_" + uuid.uuid4().hex
    options["handle_token"] = GLib.Variant("s", token)
    sender = bus.get_unique_name()[1:].replace(".", "_")
    handle = "/org/freedesktop/portal/desktop/request/" + sender + "/" + token
    result = []
    loop = GLib.MainLoop()

    def response(_bus, _sender, _path, _interface, _signal, params):
        result.append(params.unpack())
        loop.quit()

    sub = bus.signal_subscribe(DEST, "org.freedesktop.portal.Request", "Response", handle, None, Gio.DBusSignalFlags.NONE, response)
    try:
        bus.call_sync(DEST, PATH, IFACE, method, GLib.Variant(signature, (*values, options)), None, Gio.DBusCallFlags.NONE, 30000, None)
        if not result:
            loop.run()
        code, payload = result[0]
        if code != 0:
            raise RuntimeError("Screen capture permission cancelled or denied")
        return payload
    finally:
        bus.signal_unsubscribe(sub)


TOKEN = os.path.join(os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config"), "zenithclipper", "portal-restore-token")


def load_token():
    try:
        with open(TOKEN) as f:
            return f.read().strip() or None
    except OSError:
        return None


def save_token(token):
    if not token:
        return
    try:
        os.makedirs(os.path.dirname(TOKEN), exist_ok=True)
        fd = os.open(TOKEN, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w") as f:
            f.write(token)
    except OSError:
        pass


def main():
    global session, pipeline, remote_fd
    encoder, fps, quality, width = sys.argv[1:5]
    fps, quality, width = int(fps), int(quality), int(width)
    created = request("CreateSession", "(a{sv})", (), {"session_handle_token": GLib.Variant("s", "zenithclipper_" + uuid.uuid4().hex)})
    session = created["session_handle"]
    # Remember the choice (persist_mode 2) so the screen picker only shows the first time. Tokens are single use,
    # so each Start hands back a new one to save. Older portals ignore these options and just ask every time.
    sources = {"types": GLib.Variant("u", 1), "multiple": GLib.Variant("b", False), "cursor_mode": GLib.Variant("u", 2), "persist_mode": GLib.Variant("u", 2)}
    saved = load_token()
    if saved:
        sources["restore_token"] = GLib.Variant("s", saved)
    request("SelectSources", "(oa{sv})", (session,), sources)
    started = request("Start", "(osa{sv})", (session, ""), {})
    save_token(started.get("restore_token"))
    node, _properties = started["streams"][0]
    reply, fds = bus.call_with_unix_fd_list_sync(DEST, PATH, IFACE, "OpenPipeWireRemote", GLib.Variant("(oa{sv})", (session, {})), GLib.VariantType.new("(h)"), Gio.DBusCallFlags.NONE, 30000, None, None)
    remote_fd = fds.get(reply.unpack()[0])
    # Avoid implicit software fallback: hardware driver/plugin availability is explicit.
    encode = {
        "vaapi": f"vah264enc qp={quality} key-int-max={fps * 2}",
        "nvenc": f"nvh264enc preset=low-latency-hp gop-size={fps * 2} rc-mode=constqp qp-const={quality}",
        "qsv": f"qsvh264enc gop-size={fps * 2}",
        "software": f"x264enc speed-preset=ultrafast tune=zerolatency key-int-max={fps * 2} quantizer={quality}",
    }.get(encoder)
    if encode is None:
        raise RuntimeError("No supported Wayland encoder selected")
    size = f",width={width},pixel-aspect-ratio=1/1" if width else ""
    description = f"pipewiresrc fd={remote_fd} path={node} do-timestamp=true ! queue max-size-buffers=4 max-size-bytes=0 max-size-time=0 ! videoconvert ! videoscale ! videorate ! video/x-raw,format=NV12,framerate={fps}/1{size} ! {encode} ! h264parse ! matroskamux streamable=true ! fdsink fd=1 sync=false"
    pipeline = Gst.parse_launch(description)
    loop = GLib.MainLoop()
    errors = []

    def message(_bus, msg):
        if msg.type == Gst.MessageType.ERROR:
            err, debug = msg.parse_error()
            errors.append(str(err) + " " + str(debug))
            loop.quit()
        elif msg.type == Gst.MessageType.EOS:
            loop.quit()

    gst_bus = pipeline.get_bus()
    gst_bus.add_signal_watch()
    gst_bus.connect("message", message)
    GLib.unix_signal_add(GLib.PRIORITY_DEFAULT, signal.SIGTERM, lambda: (loop.quit(), False)[1])
    def engine_alive():
        if os.getppid() != parent:
            loop.quit()
            return False
        return True
    GLib.timeout_add_seconds(1, engine_alive)
    pipeline.set_state(Gst.State.PLAYING)
    loop.run()
    if errors:
        raise RuntimeError(errors[0])


try:
    main()
except Exception as exc:
    print("Wayland capture failed: " + str(exc), file=sys.stderr)
    sys.exit(1)
finally:
    if pipeline:
        pipeline.set_state(Gst.State.NULL)
    if session:
        try:
            bus.call_sync(DEST, session, "org.freedesktop.portal.Session", "Close", None, None, Gio.DBusCallFlags.NONE, 2000, None)
        except Exception:
            pass
    if remote_fd is not None:
        os.close(remote_fd)
