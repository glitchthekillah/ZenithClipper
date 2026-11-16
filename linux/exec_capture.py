#!/usr/bin/env python3
"""Runs FFmpeg for the engine and stops it if the engine process dies.

PR_SET_PDEATHSIG alone is not enough: Linux fires it when the *thread* that started us exits, and .NET retires
idle thread-pool threads after about a minute, which killed capture mid-recording. So this helper stays (a few MB),
watches the engine's process id, forwards stop signals, and FFmpeg's own parent-death signal points at this
single-threaded helper, which only exits after FFmpeg does.
"""
import ctypes
import os
import shutil
import signal
import sys
import time

engine = os.getppid()
if engine == 1:
    sys.exit(1)
program = shutil.which(sys.argv[1])
if not program:
    raise FileNotFoundError(sys.argv[1])

child = os.fork()
if child == 0:
    libc = ctypes.CDLL(None, use_errno=True)
    libc.prctl(1, signal.SIGTERM, 0, 0, 0)  # our parent is this helper's only thread, so this fires only if it dies
    if os.getppid() == 1:
        os._exit(1)
    os.execv(program, sys.argv[1:])

def forward(signum, _frame):
    try:
        os.kill(child, signum)
    except ProcessLookupError:
        pass

for s in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
    signal.signal(s, forward)

while True:
    try:
        pid, status = os.waitpid(child, os.WNOHANG)
    except ChildProcessError:
        sys.exit(1)
    if pid == child:
        sys.exit(os.waitstatus_to_exitcode(status) & 0xFF if os.WIFEXITED(status) else 128 + os.WTERMSIG(status))
    if os.getppid() != engine:  # engine gone: stop FFmpeg, then leave
        forward(signal.SIGTERM, None)
        time.sleep(2)
        forward(signal.SIGKILL, None)
    time.sleep(0.5)
