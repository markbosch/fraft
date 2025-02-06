module KVServer

  open System
  open System.Collections.Concurrent
  open System.Net
  open System.Net.Sockets
  open System.Threading.Tasks
  open FSharpx.Control

  open Raft.Api
  open Raft.Config
  open Raft.Message
  open KVApp

  let private getAddress node =
      kvservers |> Map.find node
  
  let splitAtFirstSpace (input: string) =
    let parts = input.Split([|' '|]) |> Array.toList
    match parts with
    | head :: tail -> (head, String.Join(" ", tail))
    | [] -> ("", "")

  let mutable pendingTransactions = new ConcurrentDictionary<string, TaskCompletionSource<string>>()

  let private handleClient (sock : Socket) (raft: Raft) =
    async {
      try
        while true do
          let command = recvMessage sock
          printfn "Received: %s" command
          let transactionId, kvcmd = splitAtFirstSpace command
          // Would be better to return a response
          // which then can be send over the wire
          if kvcmd.StartsWith "get" then
            if raft.IsLeader () then
              let result = handleMessage kvcmd
              sendMessage sock (b result)
            else
              sendMessage sock (b "not leader")
          else
      
            pendingTransactions[transactionId] <- new TaskCompletionSource<string>();
            if (raft.Submit command) then
              printfn "Submitted command to raft"
              let! response =
                pendingTransactions[transactionId].Task
                |> Async.AwaitTask
 
              sendMessage sock (b response)
            else
              sendMessage sock (b "not leader")
            
            pendingTransactions.TryRemove(transactionId) |> ignore
            

      with
        | ex -> printfn "Error: %s" ex.Message

      sock.Close()
      sock.Dispose()
    }

  let callback cmd =
      let transactionId, kvcmd = splitAtFirstSpace cmd
      printfn "APPLYING %s" kvcmd
      let result = handleMessage kvcmd
      match pendingTransactions.TryGetValue(transactionId) with
      | true, tcs ->
        async { 
          tcs.SetResult result }
          |> Async.Start
      | false, _ ->
        printfn "TransactionId '%s' not found" transactionId
    

  let run nodeNum =
    let raft = Raft.Api.initialize nodeNum callback
    raft.Start ()

    let address = getAddress nodeNum
    use sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
    sock.Bind address
    sock.Listen ()
    printfn "KVServer is running at %A" address
    while true do
      let client = sock.Accept()
      printfn "Connection from %A" client.RemoteEndPoint
      Async.Start(handleClient client raft)
      
