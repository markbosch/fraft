# Raft

A F# implementation of the [Raft](https://raft.github.io/raft.pdf)
consensus algorithm.

The basic idea behind Raft is to provide a consensus module in your
application, which will ensures that the state of the application
gets replicated across multiple machines via a replicated log.
This makes the application a fault-tolerant system.

This simplified implementation supports the following:

- Log replication
- Leader election

Out-of-scope:

- Snapshots
- Configuration changes
- Persistence on disk

This implementation is heavily inspired by the course I took from
David Beazley [Rafting Trip](https://www.dabeaz.com/raft.html) which I would
highly recommend to everyone interested in distributed systems.

The main reason for this implementation was to learn F# and to explore
the functional programming paradigm. Raft is an excellent problem
set for this. 

## Running the application

Start the application - a key-value server - with the following commands
(in different shells):

```shell
dotnet run 0
dotnet run 1
dotnet run 2
```

Besides the key-value server, the raft servers will also start and do
a leader election.

Start the client with the following command:

```shell
dotnet fsi KVClient.fsx
```

The problem of the client is to connect to the key-value server
which acts as the `Leader`. If it sends a command to the server
and the server is not the leader, the client will connect to the
next server.

The client supports commands like:

``` shell
KV > set x 42
KV > get x
KV > delete x
```