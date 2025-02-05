module RaftNet

  open System.Net.Sockets
  open System.Threading 
  open FSharpx.Control
  open MBrace.FsPickler.Json

  open RaftConfig
  open Message
  
  type bytes = byte array
  type Node = int

  let encode<'T> (value: 'T) : string =
    let jsonSerializer = FsPickler.CreateJsonSerializer(indent=false)
    jsonSerializer.PickleToString(value)
  
  let decode<'T> (serialized: string) : 'T =
    let jsonSerializer = FsPickler.CreateJsonSerializer()
    jsonSerializer.UnPickleOfString<'T>(serialized)

  type State = {
    NodeNum: Node;
    Inbox: BlockingQueueAgent<string>;
    Outboxes: Map<int, BlockingQueueAgent<string>>;
  }

  type RaftNet =
    {
      Send    : Node -> string -> unit
      Receive : unit -> string 
      Start   : unit -> unit
    }

  let public createRaftNet (nodeNum: Node) : RaftNet =
    let state = {
      NodeNum = nodeNum
      Inbox = new BlockingQueueAgent<string>(1)
      Outboxes = RaftConfig.servers 
        |> Map.map (fun _ _ -> new BlockingQueueAgent<string>(1))
    }
    let socket = new Socket(
      AddressFamily.InterNetwork,
      SocketType.Stream,
      ProtocolType.Tcp)
      
    let receiver sock =
      try
        while true do
          let msg = recvMessage sock
          state.Inbox.Add msg
      with
      | (ex: exn) -> sock.Close ()
        
    let acceptor () =
      socket.Bind (getAddress nodeNum)
      socket.Listen 10
      while true do
        let client = socket.Accept()
        let thread = new Thread(
          ThreadStart(fun () -> receiver client))
        thread.Start()  

    let senderAsync (dest: Node) =
      async {
        use socket = new Socket(
          AddressFamily.InterNetwork,
          SocketType.Stream,
          ProtocolType.Tcp)

        let outbox = state.Outboxes
                  |> Map.find dest
      
        while true do  
          let msg = outbox.Get()
          try
            if not socket.Connected then
              socket.ConnectAsync (getAddress dest)
              |> Async.AwaitTask
              |> ignore

            return sendMessage socket (b msg)
          with 
          | (ex: exn) -> eprintfn "%s" ex.Message }

    let sender' (dest: Node) =
      senderAsync dest |> Async.RunSynchronously

    let sender (dest : Node) =
      use socket = new Socket(
        AddressFamily.InterNetwork,
        SocketType.Stream,
        ProtocolType.Tcp)
      let outbox = state.Outboxes
                  |> Map.find dest
      
      while true do  
        let msg = outbox.Get()
        try
          if not socket.Connected then
            socket.Connect (getAddress dest)

          sendMessage socket (b msg)
        with
        | (ex: exn) -> eprintfn "%s" ex.Message

    // -- External API 
    let send (dest: Node) (msg: string) : unit =
      state.Outboxes
      |> Map.find dest
      |> fun outbox -> outbox.Add msg

    let receive () : string =
      async {
        return! state.Inbox.AsyncGet()
      } |> Async.RunSynchronously

    let start () : unit =
      let thread = new Thread(acceptor)
      thread.Start()
      state.Outboxes
      |> Map.iter (fun dest _ ->
          let thread = new Thread(ThreadStart(fun () -> sender' dest))
          thread.Start()) 

    {
      Send    = send
      Receive = receive
      Start   = start
    }

  module RaftConsole =
    open System

    open State
    open RaftLog
    open RaftLogic

    let heartbeat source dest raft =
      { Source = source
        Dest = dest
        Term = raft.CurrentTerm
        Payload = AE { PrevLogIndex = 0
                       PrevLogTerm = -1
                       Entries = [ ]
                       LeaderCommit = raft.CommitIndex }
        Timestamp = 0 }

    let leader (raft:RaftState) =
      { raft with Role = Leader }

    let console (nodeNum: Node) clusterSize =

        let (n: int) = nodeNum
        let mutable raftState = RaftLogic.initialRaftState n clusterSize
        let raftLog = RaftLog.initialize ()
        let raft = RaftLogic.initialize raftLog.AppendEntries
        let raftNet = createRaftNet nodeNum
        raftNet.Start ()

        let receiver () =
          while true do 
            let msg = decode<RaftLogic.Message> (raftNet.Receive ())
            let messages, raftState = runS (raft.HandleMessage msg) raftState
            printfn "received: %A" msg
            printfn "outgoing: %A" messages
            for message in messages do
              raftNet.Send msg.Source (encode message)
        
        let thread = new Thread(receiver)
        thread.Start()

        while true do
          printfn "Node %d > " nodeNum
          let input = Console.ReadLine ()
          
          match input.Split([| ' ' |], 2) with
          | [| dest; cmd |] ->
            match cmd with
            | "heartbeat" ->
              let msg = heartbeat (int nodeNum) (int dest) raftState
              raftNet.Send (int dest) (encode msg)
              ()

            | "leader" ->
              raftState <- leader raftState
              ()

            | "state" ->
              printfn "State: %A" raftState
              ()

            | _ ->
              failwith "Unknow command"  
            
          | _ -> failwith "Input does not contain exactly two parts"

          
