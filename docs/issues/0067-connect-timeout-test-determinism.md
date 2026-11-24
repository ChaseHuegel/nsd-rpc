# Bug: Connect timeout test assumes TEST-NET-1 is unroutable

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`ConnectTimeoutBoundsABlackholedRoute` connected to `192.0.2.1` on port 9.
The test expected a `TimeoutException`. That outcome held only when the
network dropped the traffic.

An outbound proxy accepts the connect and closes the connection. The client
then fails the handshake with `InvalidDataException`. The test fails on a host
behind a proxy, a captive portal, or a network that answers TEST-NET-1.

## Acceptance criteria

- [x] The test passes on a host behind an outbound TCP proxy.
- [x] The test fails if `ConnectTimeout` stops bounding the call.
- [x] The test does not send traffic to a non-local address.

## Resolution

`TcpClientConnection` takes an internal connector delegate. A null connector
connects the socket to the configured endpoint
([TcpClientConnection.cs:33](../../NsdRpc.Tcp/TcpClientConnection.cs:33)).
The test passes a connector that never completes
([TcpIntegrationTests.cs:897](../../NsdRpc.Tests/TcpIntegrationTests.cs:897)).
The connect deadline then expires without network traffic. The test fails
within a fixed bound if the deadline stops working.
