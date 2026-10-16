# Bug: MaxFrameBytes semantics disagree and the default blocks valid frames

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`RpcOptions.MaxFrameBytes` defaults to 65,536. The protocol doc says the
default sits above the format maximum and never blocks a valid frame
([rpc-protocol.md:127](../specs/rpc-protocol.md)). The largest valid frame is
4 + 65,566 = 65,570 bytes. A payload above 65,501 bytes is rejected on send
([RpcPeer.cs:97](../../NsdRpc/Engine/RpcPeer.cs:97)) and on receive
([RpcPeer.cs:292](../../NsdRpc/Engine/RpcPeer.cs:292)).

The layers also disagree on what the limit counts.

- `TcpFrameDecoder` compares the limit to the envelope bytes
  ([TcpFrameDecoder.cs:54](../../NsdRpc.Tcp/TcpFrameDecoder.cs:54)).
- `RpcPeer.HandleFrame` compares the limit to the whole frame, including the
  4-byte length prefix.

The two bounds differ by 4 bytes that the layers do not agree on. The
decoder accepts a payload of up to 65,505 bytes. The peer drops a payload
above 65,501 bytes. A payload of 65,502 through 65,505 bytes passes the
decoder and is dropped by the peer. A payload of 65,506 bytes or more fails
at the decoder and closes the connection.

## Acceptance criteria

- [ ] One definition states what `MaxFrameBytes` counts.
- [ ] The default admits every frame the format allows.
- [ ] Tests pin the bound at the cap and one byte over, on send and receive.
- [ ] The protocol doc and the `RpcOptions` doc match the implementation.
