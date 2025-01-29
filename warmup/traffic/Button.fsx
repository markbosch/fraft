open System
open System.Net
open System.Net.Sockets
open System.Text

let b (input : string) =
  Encoding.UTF8.GetBytes(input)

let dest (host : string) (port : int) =
  IPEndPoint(IPAddress.Parse(host), port)  

let run buttonId host port =
  let sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
  while true do
    printfn "Button %s: [Press Return]" buttonId
    Console.ReadLine() |> ignore
    sock.SendTo(b buttonId, dest host port) |> ignore

let args : string array = fsi.CommandLineArgs
if args.Length <> 4 then
  printfn "Usage: %s button-id host port" args.[0]
else
  run args.[1] args.[2] (args.[3] |> int)
