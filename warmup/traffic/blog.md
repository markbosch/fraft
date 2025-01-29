# Traffic light control

I recently got involed into a project with `EventSourcing` and
wanted to findout more about what EventSourcing is.

I came a cross a tutorial of rommson(INSERT LINK HERE) where
he builds a EventStore from scratch in F# for a ice-scream truck.

In order to project eventsourcing onto another problem set,
I wanted it to project it againts a traffic control problem which
has to be solved during the Raft course (insert link here) I
followed of David.

## Traffic light

Instead of the ice-scream truck, we'll apply eventsourcing to a simple
traffic light system. The traffic light will be a NORTH-SOUTH and EAST-
WEST setup and light colors will change by a timer.
It also contains `push buttons` which will interup the timer cycle.

Traffic light components. The [lights](githublink) can be started with
the following command. This will open a socket and will be able to
receive message to change color:

```shell
dotnet fsi Light.fsx NS 17000
NS: R
```

```shell
dotnet fsi Light.fsx EW 18000
EW: R
```

The following F# script will change the color to `G` on the NS
light.

``` fsharp
> open System.Net;;
> open System.Network;;
> open System.Text;;
> let b (input : string) = Encoding.UTF8.GetBytes(input);;
> let sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);;
> sock.SendTo(b"G", new IPEndPoint(0, 17000));;
```

In order to start the push buttons, run the following commands:

```shell
dotnet fsi Button.fsx NS "127.0.0.1" 20000
Button NS: [Press Return]
```

```shell
dotnet fsi Button.fsx EW "127.0.0.1" 20000
Button EW: [Press Return]
```

## EventStore

...

## Projection
...

## Logic
...

- Traffic light image?
- F#
- Traffic control
  - How to model a traffic light
  - Rules
  - Network
  - Event queue
- EventStore
  - Mailbox (Actor)
  - Append only
  - Subscription
    - as an replacement of the Queue
- Projection
  - fold left
- Logic
