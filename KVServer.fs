module KVServer

  open System
  open System.Collections.Concurrent
  open System.Net
  open System.Net.Sockets
  open FSharpx.Control

  open Raft
  open Message
  open KVApp
  open System.Threading.Tasks

  let private servers = Map [
      (0, IPEndPoint(0, 12345));
      (1, IPEndPoint(0, 12346));
      (2, IPEndPoint(0, 12347));
      //(3, IPEndPoint(0, 12348));
      //(4, IPEndPoint(0, 12349));
    ]

  let private getAddress node =
      servers |> Map.find node
  
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
      
            pendingTransactions[transactionId] <- new TaskCompletionSource<string>();
            if (raft.Submit command) then
              printfn "Submitted command to raft"
              let! response =
                pendingTransactions[transactionId].Task
                |> Async.AwaitTask
 
              sendMessage sock (b response)
            else
              printfn "Unable to commit to raft"
            
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
    let raft = Raft.initialize nodeNum callback
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
      
