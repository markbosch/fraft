#load "Message.fs"

open System
open System.Net
open System.Net.Sockets
open System.Text

open Message

let stress n =
  let address = IPEndPoint(0, 12345)
  use sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
  sock.Connect address
  let rec loop n =
    if n >= 0 then
      let msg = (b $"set x {n}") 
      sendMessage sock msg
      let response = recvMessage sock
      printfn "Got: %s - %A" response DateTime.UtcNow
      loop (n - 1)
  loop n

let run port () =
    let address = IPEndPoint(0, port)
    use sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
    sock.Connect address
    let rec loop () =
      Console.Write("KV > ")
      let msg = Console.ReadLine()
      if msg = "" then
        printfn "Exit"
      else
        sendMessage sock (b $"{Guid.NewGuid()} {msg}")
        let msg = recvMessage sock
        printfn "Got: %s" msg
        loop()
    loop()
  
run 12345 ()
//stress 100
