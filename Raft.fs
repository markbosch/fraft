module Raft

  open System
  open System.Diagnostics
  open System.Threading

  open State
  open RaftConfig
  open RaftNet
  open RaftLog
  open RaftLogic

  type Command = string

  type Callback = string -> unit

  type Raft =
    {
      IsLeader: unit -> bool
      Submit: Command -> bool
      Start: unit -> unit
    }

  let defaultApplicationCallback cmd =
    printfn "APPYLING %s" cmd

  let initialize (nodeNum: int) (callback:Callback) : Raft =
    let lockObj = obj()
    let mutable lastKnownQuorum = 0L
    
    let raftNet = createRaftNet nodeNum
    let raftLog = RaftLog.initialize ()
    let mutable raftState = RaftLogic.initialRaftState nodeNum (servers |> Map.count)
    if nodeNum = 0 then
      raftState <- becomeLeader raftState

    let mutable lastExchangedWithPeers =
      raftState.Peers
      |> Seq.map (fun key -> key,0L)
      |> Map.ofSeq
    
    let raftCore = RaftLogic.initialize raftLog.AppendEntries

    let isLeader () =
      let requestTime = Stopwatch.GetTimestamp ()
      lock lockObj (fun () ->
        while raftState.Role = Leader && requestTime > lastKnownQuorum do
          let timeout = TimeSpan.FromSeconds HEARTBEAT_TIMER
          Monitor.Wait(lockObj, timeout) |> ignore 
      )

      raftState.Role = Leader && requestTime < lastKnownQuorum

    let sendMessage msg =
      printfn "Sending %A: " msg // todo, think about a logger
      msg
      |> encode
      |> raftNet.Send msg.Dest
    
    let submit (command: Command) =
      if raftState.Role <> Leader then
        false
      else
        let submitCommand = { Source = nodeNum
                              Dest = nodeNum
                              Term = raftState.CurrentTerm
                              Payload = CMD { Command = command }
                              Timestamp = 0
                            }
        sendMessage submitCommand
        true

    let tagWithTimestamp timestamp imsg omsg =
      match omsg.Payload with
      | AE  _ -> { omsg with Timestamp = timestamp }
      | AER _ -> { omsg with Timestamp = imsg.Timestamp }
      | _     -> omsg

    let handleMessage msg =
      let outgoing, state = runS (raftCore.HandleMessage msg) raftState
      raftState <- state

      let result =
        outgoing
        |> List.map (tagWithTimestamp (Stopwatch.GetTimestamp ()) msg)

      match msg.Payload with
      | AER aer when raftState.Role = Leader ->
          lastExchangedWithPeers <-
            lastExchangedWithPeers
            |> Map.add msg.Source (max lastExchangedWithPeers.[msg.Source] msg.Timestamp)
          
          lock lockObj (fun () ->
            // Figure out last known quorum time.
            lastKnownQuorum <-
              lastExchangedWithPeers
              |> Map.toList
              |> List.map snd
              |> List.sort
              |> fun sortedValues -> sortedValues.[sortedValues.Length / 2]

            Monitor.PulseAll(lockObj)
          )
          
      | _ -> ()

      result     

    let runServer () =
      let mutable lastApplied = 0
      while true do
        try
          let msg = decode<RaftLogic.Message> (raftNet.Receive ())
          printfn "Received: %A" msg

          let outgoing = handleMessage msg
          
          // -- Tell the application
          while lastApplied < raftState.CommitIndex do
            lastApplied <- lastApplied + 1
            let cmd = raftState.Log.[lastApplied].Command
            callback cmd

          outgoing
          |> List.iter sendMessage
        with
        | ex -> eprintfn "Error: %s" ex.Message


    let heartbeatTimer () =
      while true do
        Thread.Sleep(TimeSpan.FromSeconds(HEARTBEAT_TIMER))
        let msg = { Source = nodeNum
                    Dest = nodeNum
                    Term = raftState.CurrentTerm
                    Payload = HB
                    Timestamp = 0
                  }
        
        sendMessage msg

    let start () =
      raftNet.Start ()
      (new Thread(runServer)).Start()
      (new Thread(heartbeatTimer)).Start()
      
    {
      IsLeader = isLeader
      Submit = submit
      Start = start
    }
