# Improvement: Shrink the TCP frame decoder after idle

- Type: improvement
- Status: done
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

- [x] The decoder shrinks to its start size after an idle period.
- [x] The shrink has one trigger. The issue states the chosen trigger and
      its own memory and CPU cost.
- [x] A stream of large frames does not thrash the grow and shrink path.
- [x] The throughput and the receive syscall count of an active connection
      do not change.
- [x] Tests pin the shrink, the idle gate, and the no-thrash rule.

## Resolution

The decoder has a shrink. `TryShrink` moves the buffer back to the start size
of 4,096 bytes
([TcpFrameDecoder.cs:44](../../NsdRpc.Tcp/TcpFrameDecoder.cs:44)). It refuses
when the decoder holds a partial frame, so the shrink never loses buffered
bytes. A lock keeps the shrink off the receive loop's push. The push and the
shrink never run at the same time.

The trigger is one idle timer per grown decoder. The connection starts the
timer when the decoder first grows above its start size
([TcpConnection.cs:333](../../NsdRpc.Tcp/TcpConnection.cs:333)). The timer
waits the idle period, then shrinks the decoder when no read happened in that
period
([TcpConnection.cs:322](../../NsdRpc.Tcp/TcpConnection.cs:322)). The timer
stops after the shrink, or on the disconnect.

Memory cost: one timer per connection, and only while its decoder is grown. A
connection that never sees a frame above the start size holds no timer. An
idle connection, after the shrink, holds neither the buffer nor the timer.
CPU cost: the timer wakes once per idle period. The wake allocates one timer
per period. Each read runs one idle check, which allocates nothing.

The idle gate reads `RpcOptions.TimestampNanos` and compares it to the
`TcpOptions.DecoderIdleShrinkAfter` period
([TcpOptions.cs:24](../../NsdRpc.Tcp/TcpOptions.cs:24)). The default is 30
seconds. Zero or negative disables the shrink.

A stream of large frames does not thrash the path. Each read resets the idle
period, so the gate never passes between the frames in the stream. The
decoder stays grown until the stream pauses for one idle period.

The throughput and the receive syscall count of an active connection do not
change. The sweep touches the decoder between reads only. It adds no socket
call and no buffer copy.
