"""Run a Windows build or validation helper inside a bounded, kill-on-close job.

Example: python windows-budget.py --memory-mib 200 --seconds 60 -- blink.exe ...
The child starts suspended; limits apply before any child code executes.
Memory is aggregate committed memory for the entire child process tree.
"""
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
import subprocess
import sys
import time


def main():
    if sys.platform != "win32":
        raise RuntimeError("Windows only")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--memory-mib", type=int, default=200)
    parser.add_argument("--cpu-percent", type=int, default=0)
    parser.add_argument("--seconds", type=float, default=0)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command or args.memory_mib <= 0 or args.seconds < 0 or not 0 <= args.cpu_percent <= 100:
        parser.error("provide a command and positive memory; CPU 0..100 and seconds >= 0 (0 disables)")
    kernel = c.WinDLL("kernel32", use_last_error=True)
    uptr = c.c_size_t

    class Basic(c.Structure):
        _fields_ = [("process_time", c.c_int64), ("job_time", c.c_int64),
                    ("flags", w.DWORD), ("min_ws", uptr), ("max_ws", uptr),
                    ("processes", w.DWORD), ("affinity", uptr),
                    ("priority", w.DWORD), ("scheduling", w.DWORD)]

    class Extended(c.Structure):
        _fields_ = [("basic", Basic), ("io", c.c_uint64 * 6),
                    ("process_memory", uptr), ("job_memory", uptr),
                    ("peak_process", uptr), ("peak_job", uptr)]

    class Cpu(c.Structure):
        _fields_ = [("flags", w.DWORD), ("rate", w.DWORD)]

    class Startup(c.Structure):
        _fields_ = [("cb", w.DWORD), ("reserved", w.LPWSTR), ("desktop", w.LPWSTR),
                    ("title", w.LPWSTR), ("x", w.DWORD), ("y", w.DWORD),
                    ("xs", w.DWORD), ("ys", w.DWORD), ("xc", w.DWORD),
                    ("yc", w.DWORD), ("fill", w.DWORD), ("flags", w.DWORD),
                    ("show", w.WORD), ("reserved_size", w.WORD),
                    ("reserved_data", c.c_void_p), ("stdin", w.HANDLE),
                    ("stdout", w.HANDLE), ("stderr", w.HANDLE)]

    class Process(c.Structure):
        _fields_ = [("process", w.HANDLE), ("thread", w.HANDLE),
                    ("pid", w.DWORD), ("tid", w.DWORD)]

    signatures = {
        "CreateJobObjectW": ([c.c_void_p, w.LPCWSTR], w.HANDLE),
        "SetInformationJobObject": ([w.HANDLE, c.c_int, c.c_void_p, w.DWORD], w.BOOL),
        "QueryInformationJobObject": ([w.HANDLE, c.c_int, c.c_void_p, w.DWORD, c.c_void_p], w.BOOL),
        "CreateProcessW": ([w.LPCWSTR, w.LPWSTR, c.c_void_p, c.c_void_p, w.BOOL,
                            w.DWORD, c.c_void_p, w.LPCWSTR, c.POINTER(Startup), c.POINTER(Process)], w.BOOL),
        "AssignProcessToJobObject": ([w.HANDLE, w.HANDLE], w.BOOL),
        "GetStdHandle": ([w.DWORD], w.HANDLE),
        "SetHandleInformation": ([w.HANDLE, w.DWORD, w.DWORD], w.BOOL),
        "ResumeThread": ([w.HANDLE], w.DWORD),
        "WaitForSingleObject": ([w.HANDLE, w.DWORD], w.DWORD),
        "GetExitCodeProcess": ([w.HANDLE, c.POINTER(w.DWORD)], w.BOOL),
        "TerminateProcess": ([w.HANDLE, w.UINT], w.BOOL),
        "TerminateJobObject": ([w.HANDLE, w.UINT], w.BOOL),
        "CloseHandle": ([w.HANDLE], w.BOOL),
    }
    for name, (inputs, output) in signatures.items():
        function = getattr(kernel, name)
        function.argtypes, function.restype = inputs, output

    def check(result):
        if not result:
            raise c.WinError(c.get_last_error())
        return result

    job = check(kernel.CreateJobObjectW(None, None))
    process = Process()
    started = time.monotonic()
    timed_out = False
    try:
        limits = Extended()
        limits.basic.flags = 0x2000 | 0x200  # kill on close; aggregate memory
        limits.job_memory = args.memory_mib * 1024 * 1024
        check(kernel.SetInformationJobObject(job, 9, c.byref(limits), c.sizeof(limits)))
        cpu = Cpu(1 | 4, args.cpu_percent * 100)  # enabled hard cap
        if args.cpu_percent:
            check(kernel.SetInformationJobObject(job, 15, c.byref(cpu), c.sizeof(cpu)))
        startup = Startup()
        startup.cb, startup.flags = c.sizeof(startup), 0x100
        for field, number in [("stdin", -10), ("stdout", -11), ("stderr", -12)]:
            handle = kernel.GetStdHandle(number & 0xffffffff)
            check(kernel.SetHandleInformation(handle, 1, 1))
            setattr(startup, field, handle)
        line = c.create_unicode_buffer(subprocess.list2cmdline(command))
        check(kernel.CreateProcessW(None, line, None, None, True,
                                    4 | 0x08000000 | 0x4000, None, None,
                                    c.byref(startup), c.byref(process)))
        check(kernel.AssignProcessToJobObject(job, process.process))
        if kernel.ResumeThread(process.thread) == 0xffffffff:
            raise c.WinError(c.get_last_error())
        while True:
            status = kernel.WaitForSingleObject(process.process, 100)
            if status == 0:
                break
            if status != 258:
                raise c.WinError(c.get_last_error())
            if args.seconds and time.monotonic() - started >= args.seconds:
                timed_out = True
                check(kernel.TerminateJobObject(job, 124))
                break
        check(kernel.QueryInformationJobObject(job, 9, c.byref(limits), c.sizeof(limits), None))
        code = w.DWORD()
        check(kernel.GetExitCodeProcess(process.process, c.byref(code)))
        print(json.dumps({"resource_summary": {"pid": process.pid,
              "peak_job_commit_bytes": limits.peak_job, "memory_limit_mib": args.memory_mib,
              "cpu_limit_percent": args.cpu_percent, "elapsed_seconds": round(time.monotonic()-started, 3),
              "timed_out": timed_out, "exit_code": code.value}}), file=sys.stderr)
        return 124 if timed_out else code.value
    finally:
        kernel.TerminateJobObject(job, 125)
        if process.process:
            kernel.TerminateProcess(process.process, 125)
        kernel.CloseHandle(job)
        for handle in (process.thread, process.process):
            if handle:
                kernel.CloseHandle(handle)


if __name__ == "__main__":
    sys.exit(main())
