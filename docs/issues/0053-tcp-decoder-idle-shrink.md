# Improvement: Shrink the TCP frame decoder after idle

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The frame decoder keeps its high-water mark. `TryGrowBuffer` doubles the
buffer up to `MaxFrameBytes` and never shrinks it
([TcpFrameDecoder.cs:98](../../NsdRpc.Tcp/TcpFrameDecoder.cs:98)). One
maximum frame grows the buffer to 65,570 bytes. The buffer stays at 65,570
after the frame is gone.

Issue [0042](0042-idle-tcp-connection-memory.md) measures the cost and
accepts design C. One maximum frame grows the live heap by 62,196 bytes per
connection. A connection that once saw a maximum frame holds 136,657 live
bytes while idle.

## Acceptance criteria

- [ ] The decoder shrinks to its start size after an idle period.
- [ ] The shrink has one trigger. The issue states the chosen trigger and
      its own memory and CPU cost.
- [ ] A stream of large frames does not thrash the grow and shrink path.
- [ ] The throughput and the receive syscall count of an active connection
      do not change.
- [ ] Tests pin the shrink, the idle gate, and the no-thrash rule.
