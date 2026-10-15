# nsd Schemas and Wire Format

One subject: the nsd schema files in this repo, the types the needlefish
source generator produces from them, and the wire format they define.

## Source of truth

Schemas live in `NsdRpc/Nsd/*.nsd`. The generated code is a product. Never
edit it. Change the schema instead.

Current schemas:

- [NsdRpc/Nsd/Messages.nsd](../../NsdRpc/Nsd/Messages.nsd:1) — the
  `Handshake` message.
- [NsdRpc/Nsd/Rpc.nsd](../../NsdRpc/Nsd/Rpc.nsd:1) — `RpcError` and
  `Envelope`, the RPC wire types. See
  [rpc-protocol](rpc-protocol.md).

The `Handshake` message:

```nsd
#version 1.0;
#namespace NsdRpc.Wire;

message Handshake
{
    int ProtocolVersion;
}
```

The `#namespace` directive sets the C# namespace of the generated code. Here
that namespace is `NsdRpc.Wire`.

## Language subset

The needlefish README documents the full nsd language. This repo uses this
subset. See
[needlefish](https://github.com/ChaseHuegel/needlefish#readme) for details.

- A file starts with `#version 1.0;` and a `#namespace` directive.
- A `message` holds typed fields. Primitives: `bool`, `int`, `long`, `float`,
  `double`, `string`. Messages and enums can be field types too.
- A field can be optional with `?`. Optional fields with no value are not
  encoded.
- A field can be an array with `[]`. Arrays hold 0 to 65,535 values in order.
- A field can set its wire ID with `= n;`. Without it, IDs follow field order
  starting at 0. Keep IDs stable across versions.
- An `enum` holds `ushort` constants. `= n;` sets the constant value.

## Generated shape

The generator produces one C# struct per message at compile time. Each struct
has:

- One public field per schema field.
- A constructor taking every field.
- `GetSize()` for the encoded byte length.
- `Serialize()` and `SerializeInto(...)` overloads for `byte[]` and spans.
- `Deserialize(...)` and `Unpack(...)` overloads for `byte[]` and spans.

The generated code uses raw pointers. The library project therefore sets
`AllowUnsafeBlocks`. See [NsdRpc/NsdRpc.csproj:9](../../NsdRpc/NsdRpc.csproj).

## Wire format notes

The generated code is the truth for encoding. It writes each field as a
`ushort` field ID followed by the value. It reads little-endian on
little-endian machines and swaps on big-endian machines. Do not re-derive
these rules in hand-written code. Call the generated methods.

## Adding a type

1. Add the message or enum to a `*.nsd` file in `NsdRpc/Nsd/`.
2. Build. The generator produces the type.
3. Pin the behavior with a test in `NsdRpc.Tests`.

The round-trip contract is pinned by
[NsdRpc.Tests/HandshakeTests.cs](../../NsdRpc.Tests/HandshakeTests.cs:8)
and
[NsdRpc.Tests/EnvelopeTests.cs](../../NsdRpc.Tests/EnvelopeTests.cs:8).
