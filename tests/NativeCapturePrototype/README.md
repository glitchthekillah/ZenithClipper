# Native capture prototype



- No arguments: help only; no GPU/device/capture.
- `--capture --adapter 1 --seconds 5 --buffers 1`: working NVIDIA WGC capture-only floor probe.
- `--capture --encode --adapter 1 --seconds 5 --buffers 2 --fps 60`: experimental MediaTranscoder diagnostic; measured >138 MiB, stalls with retained WGC frames, no validated output. Do not use as recorder.

The adapter index must be verified per machine. All capture runs must be serialized with other GPU experiments. This project has no audio capture or QP-20-equivalent encoder configuration. Logs are evidence from a bounded feasibility experiment, not production acceptance tests.

Direct NVENC is now available via `--capture --nvenc --adapter 1 --seconds 5 --buffers 1 --fps 165` after `build-native.ps1`. It successfully produced 825 decodable native-resolution CQP-20 frames in ~5 seconds. The JIT build measured 115.72 MiB; it does not meet the full-recorder memory target.
