# Improvement: Notification proxies discard the send result

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`IRpcCaller.Notify` returns a `bool` that reports a rejected send. See
[IRpcCaller.cs:36](../../NsdRpc/Engine/IRpcCaller.cs:36) and `:39`.
`RpcPeer.Notify` implements it at
[RpcPeer.cs:311](../../NsdRpc/Engine/RpcPeer.cs:311) and `:328`.

The generator emits every notification proxy as `public void` and discards the
return value. See
[RpcServiceGenerator.cs:699](../../NsdRpc.Generators/RpcServiceGenerator.cs:699).
A caller that uses the typed proxy cannot tell a delivered notification from a
rejected one. A rejected snapshot, heartbeat, chat relay, or voxel edit is
visible only off the typed surface. The peer then desynchronizes silently.

## Acceptance criteria

- [x] A notification method declared `bool NotifyX(Request request)` compiles.
- [x] Its proxy returns `true` when `Notify` accepts the frame and `false` when
      it rejects the frame.
- [x] The paired server handler remains `void` and dispatches unchanged.
- [x] Existing `void` notification proxies keep compiling and behaving.
- [x] Generator tests cover the `void` and `bool` notification shapes.
- [x] A runtime test forces a rejected send and observes `false` through the
      proxy.
- [x] `RSG1006` accepts the new shape. A `bool` handler shape is `RSG1012`.
