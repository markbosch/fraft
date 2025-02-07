namespace Raft

module Api =

  open System
  open System.Diagnostics
  open System.Threading

  open State
  open Config
  open RaftNet
  open RaftLog
  open RaftController

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
    let mutable heardFromLeader = false
    
    let raftNet = createRaftNet nodeNum
    let raftLog = RaftLog.initialize ()
    let mutable raftState = RaftController.initialRaftState nodeNum (servers |> Map.count)

    let mutable lastExchangedWithPeers =
      raftState.Peers
      |> Seq.map (fun key -> key,0L)
      |> Map.ofSeq
    
    let controller = RaftController.initialize raftLog.AppendEntries

    let isLeader () =
      let requestTime = Stopwatch.GetTimestamp ()
      lock lockObj (fun () ->
        while raftState.Role = Leader && requestTime > lastKnownQuorum do
          let timeout = TimeSpan.FromSeconds HEARTBEAT_TIMER
          Monitor.Wait(lockObj, timeout) |> ignore 
      )

      raftState.Role = Leader && requestTime < lastKnownQuorum

    let sendMessage msg =
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

    let tagWithTimestamp timestamp omsg =
      { omsg with Timestamp = timestamp }

    let processOutgoingMsg imsg omsg =
      match omsg.Payload with
      | AE  _ ->
        tagWithTimestamp (Stopwatch.GetTimestamp ()) omsg

      | AER _ when omsg.Term = imsg.Term ->
        heardFromLeader <- true
        tagWithTimestamp imsg.Timestamp omsg

      | AER _ ->
        tagWithTimestamp imsg.Timestamp omsg

      | RVR rvr when rvr.VoteGranted ->
        heardFromLeader <- true
        omsg
      | _     -> omsg

    let handleMessage msg =
      let outgoing, state = runS (controller.HandleMessage msg) raftState
      raftState <- state

      let result =
        outgoing
        |> List.map (processOutgoingMsg msg)

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
          let msg = decode<RaftController.Message> (raftNet.Receive ())
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

    let electionTimer () =
      let rnd = new Random()
      while true do
        heardFromLeader <- false
        let sleep = ELECTION_TIMEOUT + rnd.Next(0, ELECTION_RANDOM + 1)
        Thread.Sleep(TimeSpan.FromSeconds(sleep))
        if raftState.Role <> Leader && heardFromLeader = false then
          let callElection =
            { Source = nodeNum
              Dest = nodeNum
              Term = raftState.CurrentTerm
              Payload = CallElection
              Timestamp = 0 }
          sendMessage callElection

    let start () =
      raftNet.Start ()
      (new Thread(runServer)).Start ()
      (new Thread(heartbeatTimer)).Start ()
      (new Thread(electionTimer)).Start ()
    {
      IsLeader = isLeader
      Submit = submit
      Start = start
    }
