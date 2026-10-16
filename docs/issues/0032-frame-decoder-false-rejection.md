# Bug: Frame decoder rejects valid data near the frame cap

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`TcpFrameDecoder.TryPush` grows the buffer when `_count + data.Length`
exceeds the buffer length. The growth clamps the new size to
`MaxFrameBytes + 4`. When the clamped size is still below the needed size,
`TryPush` returns `false`:
[TcpFrameDecoder.cs:26](../../NsdRpc.Tcp/TcpFrameDecoder.cs:26).

The receive loop treats `false` as fatal and closes the connection
([TcpConnection.cs:171](../../NsdRpc.Tcp/TcpConnection.cs:171)).

The needed size can exceed the cap for valid input. One socket read can hold
the tail of one frame and the head of the next frame. `TcpOptions` also lets
the host raise `ReceiveBufferBytes` above the frame cap
([TcpOptions.cs:18](../../NsdRpc.Tcp/TcpOptions.cs:18)).

Example: the buffer holds 65,540 bytes and `_count` is 65,530. The next read
returns 100 bytes. The needed size is 65,630. The cap is 65,540. `TryPush`
returns false on valid data.

The cap must bound one frame. The cap must not reject the accumulated
buffer.

## Acceptance criteria

- [x] A test pushes one maximum-size frame in two parts. The second part holds the start of the next frame. Both frames arrive and `TryPush` returns true.
- [x] A test pushes one span above the frame cap that holds one complete frame and the start of the next frame. The decoder accepts both frames.
- [x] An oversize frame still returns false. A zero-length frame still returns false.
- [x] The buffer growth has a stated upper bound. The decoder asserts the bound.
- [x] The fuzz test covers split points near the cap and stays green.

## Resolution

`TryPush` copies the read in slices that fit the buffer and emits complete
frames after each slice. The retained bytes never exceed one frame.
`TryGrowBuffer` stops at the frame bound, and the decoder asserts the bound
with the shared `Invariant` helper.
