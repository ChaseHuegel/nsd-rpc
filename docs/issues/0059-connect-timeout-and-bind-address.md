# Improvement: Connect timeout and bind address

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`TcpClientConnection.ConnectAsync` uses `Socket.ConnectAsync(host, port)`
without a deadline
([TcpClientConnection.cs:68](../../NsdRpc.Tcp/TcpClientConnection.cs:68)).
A blackholed route waits out the OS retry schedule.

`TcpServerTransport.EnsureListener` binds `IPAddress.Any`
([TcpServerTransport.cs:192](../../NsdRpc.Tcp/TcpServerTransport.cs:192)).
A host cannot restrict the listener to one interface.

## Acceptance criteria

- [x] `TcpOptions.ConnectTimeout` defaults to 10 seconds. Zero or negative
      disables it. The key appears in the docs tables.
- [x] `ConnectAsync` honors the deadline. On expiry, it disposes the socket
      and throws `TimeoutException` with the endpoint.
- [x] `TcpOptions.BindAddress` defaults to `IPAddress.Any`. A null value
      fails fast.
- [x] `TcpServerTransport` uses the bind address. `Port` still reports the
      bound port.
- [x] A test proves a blackholed connect times out within the budget.
- [x] A test proves a listener bound to loopback accepts a loopback client.
- [x] A test proves `Port` works with a non-default bind address.
- [x] The docs pass updates the TCP options tables and `rpc-api`.
- [x] No wire change.

## Implementation notes

- Keep the existing parameterless `ConnectAsync` signature. The timeout is a
  `TcpOptions` value.
- Validate the address in the constructor or at connect time. Do not defer a
  null failure to the socket.
- A later token overload can link the caller token to the deadline.

## Resolution

`TcpOptions.ConnectTimeout` defaults to 10 seconds. `ConnectAsync` races the
connect against the deadline, disposes the socket on expiry, and throws
`TimeoutException` with the endpoint. `TcpOptions.BindAddress` defaults to
`IPAddress.Any`, and a null value fails fast at transport construction.
`TcpServerTransport` binds the configured address and `Port` reads the bound
endpoint.
