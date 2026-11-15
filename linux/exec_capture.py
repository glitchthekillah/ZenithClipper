#!/usr/bin/env python3
"""Replace this startup-only helper with FFmpeg, terminating it if its parent engine dies."""
import ctypes
import os
import shutil
import signal
import sys

parent = os.getppid()
libc = ctypes.CDLL(None, use_errno=True)
if libc.prctl(1, signal.SIGTERM, 0, 0, 0) != 0:
    raise OSError(ctypes.get_errno(), "Could not set capture parent-death signal")
if parent == 1 or os.getppid() != parent:
    sys.exit(1)
program = shutil.which(sys.argv[1])
if not program:
    raise FileNotFoundError(sys.argv[1])
os.execv(program, sys.argv[1:])
