namespace Raft

module Message =

  open System.Net
  open System.Net.Sockets
  open System.Text

  let b (msg : string) =
    Encoding.UTF8.GetBytes(msg)

  let s (bytes : byte[]) =
    Encoding.UTF8.GetString(bytes)
    
  let sendMessage (sock : Socket) (msg : byte[]) =
    let size (msg : byte[]) =
      b $"{msg.Length,10:G}"
    
    sock.Send(size msg) |> ignore
    sock.Send(msg) |> ignore

  let recvExactly (sock: Socket) (nbytes: int) =    
    let rec receive (remaining: int) (accumulated: byte list) =
        if remaining <= 0 then
            accumulated
        else
            let buffer = Array.zeroCreate<byte> remaining
            let received = sock.Receive(buffer, 0, remaining, SocketFlags.None)
            if received = 0 then
                raise (System.IO.IOException("Incomplete message"))
            let chunk = buffer.[0..received - 1] |> List.ofArray
            receive (remaining - received) (accumulated @ chunk)

    receive nbytes []
    |> List.toArray
    |> s

  let recvMessage
    (sock : Socket) =
      let size = int (recvExactly sock 10)
      recvExactly sock size
