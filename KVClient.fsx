#load "Raft/Message.fs"
#load "Raft/RaftConfig.fs"

open System
open System.Net
open System.Net.Sockets
open System.Text

open Raft.Message
open Raft.Config

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

let rec run nodenum =
    let address = kvservers |> Map.find nodenum
    use sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
    sock.Connect address
    let rec loop () =
      Console.Write($"KV [{nodenum}] > ")
      let msg = Console.ReadLine()
      if msg = "" then
        printfn "Exit"
      else
        sendMessage sock (b $"{Guid.NewGuid()} {msg}")
        let msg = recvMessage sock
        printfn "Got: %s" msg
        // So, a bit ugly here, but the simplest way to connect
        // to another node if it's not the leader. Just try the
        // next one
        if msg = "not leader" then
          run (nodenum + 1)
        else
          loop ()
    loop ()
  
run 0
//stress 100
