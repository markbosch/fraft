open System
open System.Net
open System.Net.Sockets
open System.Text

let esc = string (char 0x1B)
let codes = Map [("R", "[31;1m"); ("G", "[32;1m"); ("Y", "[33;1m")]

let run (label : string) (port : int) =
  let address = IPEndPoint(0, port)  
  let socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
  socket.Bind address
  let rec loop label (light : string) =
    let code = codes.[light]
    Console.WriteLine(esc + $"{code}{label}: {light}" + esc + "[0m")
    let buffer: byte[] = Array.zeroCreate 1024
    let received = socket.Receive buffer
    Console.Clear()
    let msg = Encoding.UTF8.GetString(buffer, 0, received)
    if List.contains msg [ "R"; "G"; "Y" ] then
      loop label msg
    else
      loop label light

  loop label "R"
  ()


let args : string array = fsi.CommandLineArgs
if args.Length <> 3 then
  printfn "Usage: %s label port" args.[0]
else
  run args.[1] (args.[2] |> int)
  


