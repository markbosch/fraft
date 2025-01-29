# EventSourced Traffic Control

A network based Traffic Light Control system,
backed by an EventStore.

The Traffic Light Control system is able to
control traffic lights by sending network
calls to them and is able to receive interupts
of network `push buttons`. It will simulate a traffic
light intersection for a North-South and
East-West setup.

In order to start the system, you'll need to
open the following terminals:

- 2 terminals for the lights
- 2 terminals for the buttons
- 1 terminal for the traffic control

All code is written in F# and can be started
via the dotnet command lets.

Lights:

``` shell
dotnet fsi Light.fsx NS 17000
dotnet fsi Light.fsx EW 18000
```

Simple F# script to change the color of the light

``` dotnet fsi
> open System.Net;;
> open System.Network;;
> open System.Text;;
> let b (input : string) = Encoding.UTF8.GetBytes(input);;
> let sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);;
> sock.SendTo(b"G", new IPEndPoint(0, 17000));;

```

Buttons:

``` shell
dotnet fsi Button.fsx NS "127.0.0.1" 20000
dotnet fsi Button.fsx EW "127.0.0.1" 20000
```

Traffic control:

``` shell
dotnet run
```
