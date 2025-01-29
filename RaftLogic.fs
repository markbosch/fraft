module RaftLogic

  open State
  open RaftConfig
  open RaftLog

  type AppendEntries = {
    PrevLogIndex: int
    PrevLogTerm: int
    Entries: LogEntry list
    LeaderCommit: int
  }
 
  type AppendEntriesResponse = {
    Success: bool
    MatchIndex: int
  }

  type RequestVote = {
    LastLogIndex: int
    LastLogTerm: int
  }

  type RequestVoteResponse = {
    VoteGranted: bool
  }

  type SubmitCommand = {
    Command: string
  }

  type Payload =
  | AE of AppendEntries
  | AER of AppendEntriesResponse
  | RV of RequestVote
  | RVR of RequestVoteResponse
  | CMD of SubmitCommand
  | HB
  | UpdateFollowers
  | CallElection
  
  type Message = {
    Source: int
    Dest: int
    Term: int  // todo, consider moving this property to AE & AER
    Payload: Payload
    Timestamp: int64
  }

  type Role =
  | Follower
  | Candidate
  | Leader

  type RaftState = {
    NodeNum: int
    Peers: int seq
    CurrentTerm: int
    CommitIndex: int
    VotedFor: int option
    NextIndex: Map<int,int>
    MatchIndex: Map<int,int>
    Role: Role
    Log: Log
    VotesReceived: Map<int,bool>
  }

  type RaftCore = {
      HandleMessage: Message -> S<RaftState, Message list>
    }

  let private peers clusterSize nodeNum : seq<int> =
    [ for n in 0 .. clusterSize - 1 do
        if n <> nodeNum then yield n ]

  let private index (value: int) : seq<int> -> Map<int,int> =
    Seq.map (fun n -> (n, value)) >> Map.ofSeq

  let private resetVotesReceived
    (peers : seq<int>) : Map<int, bool> =
      peers
      |> Seq.map (fun key -> key, false)
      |> Map.ofSeq
  
  let becomeCandidate (raft: RaftState) : RaftState =
    printfn "%d became candidate in term %d" raft.NodeNum (raft.CurrentTerm + 1)
    // Become a candidate.
    // I vote for myself and update the current term
    // Reset the votes received
    { raft with
        Role = Candidate
        CurrentTerm = raft.CurrentTerm + 1
        VotedFor = Some raft.NodeNum
        VotesReceived = resetVotesReceived raft.Peers } 


  // temp public
  let public becomeFollower (raft: RaftState) (term: int) : RaftState =
    { raft with
        Role = Follower
        CurrentTerm = term
        VotedFor = None }

  let public becomeLeader (raft: RaftState) : RaftState =
    printfn "%d became leader in term %d" raft.NodeNum raft.CurrentTerm
    { raft with
        Role = Leader
        NextIndex = raft.Peers |> index (List.length raft.Log)
        MatchIndex = raft.Peers |> index 0 }

  let private updateCommitIndex raft leaderCommit =
    let newCommitIndex = min((raft.Log |> List.length) - 1) leaderCommit
    if newCommitIndex > raft.CommitIndex then
      printfn "FOLLOWER COMMIT: %A" (List.skip (raft.CommitIndex + 1) (List.take (newCommitIndex + 1) (raft.Log)))
      { raft with CommitIndex = newCommitIndex }
    else
      raft

  let private handleAppendEntries appendEntries term source ae : S<RaftState, Message list> = state {
    let! raft = getS

    let result, raft' =
      if term < raft.CurrentTerm then
        false, raft
      else
        let raft'' =
          if term > raft.CurrentTerm || raft.Role = Candidate then
            becomeFollower raft term
          else
            raft
        // Append entries to the log
        let success, log = runS (appendEntries ae.PrevLogIndex ae.PrevLogTerm ae.Entries) raft.Log
        success, { raft'' with Log = log }
    
    let raft'' =
      if result && ae.LeaderCommit > raft.CommitIndex then
        updateCommitIndex raft' ae.LeaderCommit
      else
        raft'

    do! putS raft''
    return [ { Source = raft.NodeNum
               Dest = source
               Term = raft.CurrentTerm
               Payload =
                 AER { Success = result
                       MatchIndex = (ae.PrevLogIndex + (ae.Entries |> List.length))
                     }
               Timestamp = 0 } ]
  }

  let private handleAppendEntriesResponseSuccess
    (raft: RaftState) (source: int) (aer: AppendEntriesResponse) : RaftState =
      let nextIndex =
        raft.NextIndex
        |> Map.add source (aer.MatchIndex + 1)

      let updatedMatchIndex =
        max (raft.MatchIndex |> Map.find source) aer.MatchIndex

      let matchIndex =
        raft.MatchIndex
        |> Map.add source updatedMatchIndex

      let commitIndex =
        let newCommitIndex =
          matchIndex
          |> Map.values
          |> Seq.sort
          |> Seq.item ((raft.Peers |> Seq.length) / 2)

        let logEntry = raft.Log |> List.item newCommitIndex
        if newCommitIndex > raft.CommitIndex && logEntry.Term = raft.CurrentTerm then
          // reached consensus
          printfn "COMMITTING index: %d" newCommitIndex
          newCommitIndex
        else
          raft.CommitIndex // no consensus yet

      { raft with CommitIndex = commitIndex; NextIndex = nextIndex; MatchIndex=matchIndex; }
  
  let private handleAppendEntriesResponseFailed (raft:RaftState) (source:int) =
    let nextIndex =
      let sourceIndex = raft.NextIndex.[source]
      if sourceIndex > 1 then
        raft.NextIndex
        |> Map.add source (sourceIndex - 1) // backup
      else
        raft.NextIndex

    let prevIndex = raft.NextIndex.[source] - 1
    let prevTerm = raft.Log.[prevIndex].Term
    let messages = [ { Source=raft.NodeNum
                       Dest=source
                       Term=raft.CurrentTerm
                       Timestamp=0L
                       Payload= AE { PrevLogIndex=prevIndex
                                     PrevLogTerm=prevTerm
                                     Entries=[]
                                     LeaderCommit=raft.CommitIndex } } ]
    messages, { raft with NextIndex = nextIndex }
    

  let private handleAppendEntriesResponse source term aer =
    state {
      let! raft = getS

      let messages, raft' =
        // Message out of date. Ignore
        if term < raft.CurrentTerm then
          [], raft
        elif aer.Success then
          // It worked          
          [], handleAppendEntriesResponseSuccess raft source aer 
        else
          // It failed.
          handleAppendEntriesResponseFailed raft source

      do! putS raft'
      return messages
    }

  let private handleSubmitCommand appendEntries command =
    state {
      let! raftState = getS
      
      let result =
        if raftState.Role = Leader then
          let logEntry = { Term = raftState.CurrentTerm; Command = command.Command }
          let prevIndex = List.length raftState.Log - 1
          let prevTerm = (List.item prevIndex raftState.Log).Term
          let log = execS (appendEntries prevIndex prevTerm [logEntry]) raftState.Log
          
          { raftState with Log = log }
        else
          raftState
      
      do! putS result
      return []
    }

  let updateFollowers state =
    let updateFollower follower =
      let prevIndex = (Map.find follower state.NextIndex) - 1
      let prevTerm = (List.item prevIndex state.Log).Term
      let entries = state.Log
                      |> List.skip (prevIndex + 1)
                      //|> List.take MAX_ENTRIES
      { Source = state.NodeNum
        Dest = follower
        Term = state.CurrentTerm
        Payload = AE { PrevLogIndex = prevIndex
                       PrevLogTerm = prevTerm
                       Entries = entries
                       LeaderCommit = state.CommitIndex
                       } 
        Timestamp = 0 }

    state.Peers
    |> Seq.toList
    |> List.map updateFollower

  let updateFollowers' =
    state {
      let! state = getS
      return updateFollowers state
    }

  let private requestVotes raftState : Message list =
    let lastLogIndex = (raftState.Log |> List.length) - 1
    let lastLogTerm = raftState.Log.[lastLogIndex].Term
    raftState.Peers
    |> Seq.map
        (fun peer ->
          { Source = raftState.NodeNum
            Dest = peer
            Term = raftState.CurrentTerm
            Payload = RV { LastLogIndex = lastLogIndex
                           LastLogTerm = lastLogTerm }
            Timestamp = 0
          })
    |> Seq.toList
  
  let handleHeartbeat =
    state {
      let! state = getS
      printfn "HandleHeartbeat state: %A" state
      if state.Role = Leader then
        let messages = updateFollowers state
        printfn "HandleHeartbeat msg: %A" messages
        return messages
      elif state.Role = Candidate then
       return requestVotes state
      else
        return [] // ignore
    }

  let private handleRequestVote
    (source:int) (term:int) (msg: RequestVote) : S<RaftState, Message list> =
      state {
        // A vote request from a candidate
        let! state = getS
        // Determine if we need to step down to a follower
        let state =
          if term > state.CurrentTerm then
            becomeFollower state term
          else
            state

        let voteGranted =
          match term, state.VotedFor with
          | term, _ when term < state.CurrentTerm -> false
          | _, Some votedFor when votedFor <> source -> false
          | _ ->
              let lastLogIndex = (List.length state.Log) - 1
              let lastLogTerm = state.Log.[lastLogIndex].Term

              match lastLogTerm, msg.LastLogTerm with
              | logTerm, msgTerm when logTerm > msgTerm -> false
              | logTerm, msgTerm when logTerm < msgTerm -> true
              | _ -> lastLogIndex <= msg.LastLogIndex

        // Update the state
        let state =
          if voteGranted then
            { state with VotedFor = Some source }
          else
            state

        do! putS state
        
        return [ { Source = state.NodeNum
                   Dest = source
                   Term = state.CurrentTerm
                   Payload = RVR { VoteGranted = voteGranted }
                   Timestamp = 0 } ] }
    
    

  let private handleRequestVoteResponse
    (source:int) (term:int) (msg:RequestVoteResponse) =
      state {
        let! state = getS
        let messages, state =
          match term with
          | term when term < state.CurrentTerm -> [],state
          | _ ->

            let totalVotes =
              state.VotesReceived
              |> Map.add source msg.VoteGranted
              |> Map.filter (fun _ voteGranted -> voteGranted)
              |> Map.count

            match state.Role with
            | Candidate when totalVotes >= (Seq.length state.Peers) / 2 ->
              // -- Enough votes received, become leader
              let state = becomeLeader state
              // -- From figure 2: "Upon election, send initial AE RPCs
              // -- to each server."
              (updateFollowers state), state
            | _ ->
              [],state

        do! putS state  
        return messages
      }

  let private handleCallElection : S<RaftState, Message list> =
    state {
      let! state = getS
      let newState = becomeCandidate state
      do! putS newState
      return requestVotes newState 
    }

  let public initialize appendEntries : RaftCore =

    let handleMessage (message: Message) : S<RaftState, Message list> =
      match message.Payload with
      // -- Raft specific messages
      | AE ae ->
        handleAppendEntries appendEntries message.Term message.Source ae 

      | AER aer ->
        handleAppendEntriesResponse message.Source message.Term aer

      | RV requestVote ->
        handleRequestVote message.Source message.Term requestVote

      | RVR requestVoteResponse ->
        handleRequestVoteResponse message.Source message.Term requestVoteResponse

      // -- Internal, no raft messages
      | CMD cmd ->
        handleSubmitCommand appendEntries cmd

      | HB ->
        handleHeartbeat

      | UpdateFollowers ->
        updateFollowers'

      | CallElection ->
        handleCallElection

    {
      HandleMessage = handleMessage
    }

  let public initialize' (raftlog:RaftLog) : RaftCore =
    initialize raftlog.AppendEntries

  let public initialRaftState nodeNum clusterSize =
    let peers' = peers clusterSize nodeNum
    let nextIndex = peers' |> index 1
    let matchIndex = peers' |> index 0
    {
      NodeNum = nodeNum
      Peers = peers'
      CurrentTerm = 1
      CommitIndex = 0
      VotedFor = None
      NextIndex = nextIndex
      MatchIndex = matchIndex
      Role = Follower
      Log = empty
      VotesReceived = resetVotesReceived peers'
    }


// -- Tests
  open Expecto

  let fakeCluster numNodes =
    seq { for n in 0 .. numNodes ->
            let state = initialRaftState n numNodes
            let raftLog = RaftLog.initialize ()
            let raft = initialize' raftLog
            raft
        }

  let entry term command = {
    Term=term;Command=command }

  let createFigure6 () =
    let entries = [ (entry 1 "x<3"); (entry 1 "y<1"); (entry 1 "y<9");
                    (entry 2 "x<2"); (entry 3 "x<0"); (entry 3 "y<7")
                    (entry 3 "x<5"); (entry 3 "x<4") ]

    fakeCluster 5

  let tests =
    testList "Test RaftLogic" [
      test "Test follower update" {
        // leader
        let cmd = { Source = 0; Dest = 0; Term=1; Payload = CMD { Command = "set x 42" }; Timestamp =0; }
        let updateFollowersMsg = { Source = 0; Dest = 0; Term = 1; Payload = UpdateFollowers; Timestamp = 0 }
        let state = initialRaftState 0 2
        let raftLog = RaftLog.initialize ()
        let leader = initialize' raftLog
        let state = becomeLeader state

        let state = execS (leader.HandleMessage cmd) state
        let outgoing, state = runS (leader.HandleMessage updateFollowersMsg) state
        
        Expect.equal outgoing [ { Source = 0
                                  Dest = 1
                                  Term = 1
                                  Payload =
                                    AE { PrevLogIndex = 0
                                         PrevLogTerm = -1
                                         Entries = [ { Term = 1; Command = "set x 42" } ]
                                         LeaderCommit = 0
                                       }
                                  Timestamp = 0
                                } ] "Outgoing"

        // Follower
        let followerState = initialRaftState 1 2
        let followerLog = RaftLog.initialize ()
        let follower = initialize' followerLog
        let response, followerState' = runS (follower.HandleMessage (outgoing.[0])) followerState
        Expect.equal response [ { Source=1
                                  Dest=0
                                  Term=1
                                  Payload =
                                    AER { Success = true
                                          MatchIndex = 1 }
                                  Timestamp = 0 } ] "Response"

        // Log of the leader and follower should be equal. It's replicated
        Expect.equal state.Log followerState'.Log "Log should be equal"

        // Next index for the follower
        Expect.equal (state.NextIndex |> Map.find 1) 1 "Next index for follower"
        let outgoing, state = runS (leader.HandleMessage response.[0]) state
        Expect.equal outgoing [] "No outgoing messages"
        Expect.equal (state.NextIndex |> Map.find 1) 2 "Next index is advanced"
      }
      test "Heartbeat" {
        // Heartbeat
        // Leader generates a heartbeat message to itself.
        // This results in - because he is the leader - outgoing
        // AppendEntries message (described in the paper as a heartbeat message)
        // which should be passed into the follower.
        // The heartbeat interval is also the "pace" at which logs gets
        // replicated to followers

        // Leader
        let leaderState = initialRaftState 0 2
        let leaderLog = RaftLog.initialize ()
        let leader = initialize' leaderLog
        let leaderState = becomeLeader leaderState
        let heartbeatMsg = { Source=0;Dest=0;Term=1;Payload=HB;Timestamp=0; }

        let outgoing,leaderState = runS (leader.HandleMessage heartbeatMsg) leaderState
        Expect.equal
          outgoing
          [{Source=0;Dest=1;Term=1;Payload=AE {PrevLogIndex=0;PrevLogTerm= -1;Entries=[];LeaderCommit=0;};Timestamp=0;}]
          "AppendEntries aka heartbeat in this case"

        // Follower
        let followerState = initialRaftState 1 2
        let followerLog = RaftLog.initialize ()
        let follower = initialize' followerLog

        let response,followerState = runS (follower.HandleMessage outgoing.[0]) followerState
        Expect.equal response [{Source=1;Dest=0;Term=1;Payload=AER {Success=true;MatchIndex=0;};Timestamp=0;}] "AppendEntriesResponse"

        // Handle the AppendEntriesResponse
        let outgoing,leaderState = runS (leader.HandleMessage response.[0]) leaderState
        Expect.equal outgoing [] "Outgoing is empty"
      }
    ]

  let runRaftLogicTests () =
    runTestsWithCLIArgs [] [||] tests |> ignore
