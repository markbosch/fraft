module RaftLog

  open State

  type LogEntry =
    {  Term: int;
       Command: string;
    }

  // todo: look into single union case
  // type Log = Log of LogEntry list
  type Log = LogEntry list

  type RaftLog =
    {
      /// prevIndex -> prevTerm -> entries -> bool
      AppendEntries: int -> int -> LogEntry list -> S<Log, bool>
      Length: unit -> S<Log, int>
      Item: int -> S<Log, LogEntry>
    }

  let empty : Log =
    [ { Term = -1; Command = "" } ]

  let item index : S<Log, LogEntry> =
    state {
      let! log = getS
      return log |> List.item index  
    }

  let length () : S<Log, int> =
    state {
      let! log = getS
      return log |> List.length
    }

  let rec resolveConflicts
    (log: list<_>) (entries: list<_>) (index: int) =
      match entries with
      | [] ->
        log

      | x::xs ->
        let n = index + 1
        if n >= log.Length || log.[n].Term <> x.Term then
          log |> List.take n
        else
          resolveConflicts log xs n
    
  // todo: think about a initialize with an initial entries
  let initialize () : RaftLog =
  
    let appendEntries prevIndex prevTerm entries : S<Log, bool> =
      state {
        let! log = getS

        // Check for gaps in the log
        if prevIndex >= List.length log then
          return false
        else
          // Enforce log continuity: terms must match
          let! entry = item prevIndex
          if entry.Term <> prevTerm then
            return false
          else  
            // Figure 2: "If an existing entry conflicts with a new one (same index,
            // but different terms), delete the existing entry and all that follow it.
            let log' = resolveConflicts log entries prevIndex
            
            // Idempotency. If any prior operation is repeated, it should
            // result in the same log
            let before = log'.[..prevIndex]
            let after = log'.[(prevIndex + 1 + (entries |> List.length))..]
            do! putS (before @ entries @ after)
            return true
    }

    {
      AppendEntries = appendEntries
      Length = length
      Item = item
    }

  open Expecto

  let entry term command =
    {Term=term;Command=command}

  
  let tests =
    testList "test log" [
      test "test RaftLog" {
         let raftLog = initialize()

         let getLength log =
           evalS (raftLog.Length ()) log

         let getItem index log =
           evalS (raftLog.Item index) log
           
         let getPrevTerm index log =
           (getItem index log).Term
           

         // Add first entry
         let length      = getLength empty
         let prevIndex   = length - 1
         let prevTerm    = getPrevTerm prevIndex empty
         let result, log = runS (raftLog.AppendEntries prevIndex prevTerm [entry 1 "x"]) empty
         Expect.isTrue result "Append LogEntry successful"
         Expect.equal (getItem 1 log) {Term=1; Command="x"} "Entry equal"

         // Add multiple entries
         let length      = getLength log
         let prevIndex   = length - 1
         let prevTerm    = getPrevTerm prevIndex log
         let entries     = [entry 1 "y"; entry 1 "z"; entry 2 "a"]
         let result, log = runS (raftLog.AppendEntries prevIndex prevTerm entries) log
         Expect.isTrue result "Appended multiple entries successful"
         
         // Add no entries
         let result, log = runS (raftLog.AppendEntries prevIndex prevTerm []) log
         Expect.isTrue result "Append no entries successful"

         // Prior operation
         let result, log = runS (raftLog.AppendEntries 0 -1 [ entry 1 "x" ]) log
         Expect.isTrue result "Append prior operations. Idempotency"
         Expect.sequenceEqual (log |> List.tail) [ entry 1 "x"; entry 1 "y"; entry 1 "z"; entry 2 "a"] "Idempotency"

         // No gaps are allowed
         let result, log = runS (raftLog.AppendEntries 10 2 [entry 2 "b"]) log
         Expect.isFalse result "No gaps are allowed"  

         // Terms of prior entry must match
         let length = evalS (raftLog.Length ()) log
         let result, log = runS (raftLog.AppendEntries (length - 1) 1 [entry 2 "c"]) log
         Expect.isFalse result "Terms of prior entry must match"

         // Entry doesn't match with what is already in the log -> delete
         let result, log = runS (raftLog.AppendEntries 1 1 [ entry 3 "a"]) log
         Expect.isTrue result "Entry doesn't match with whats already in the log"
         Expect.sequenceEqual (log |> List.tail) [ entry 1 "x"; entry 3 "a"] "Resolve conflicts"
      }
    ]
  
  let runLogTests () =
    runTestsWithCLIArgs [] [||] tests |> ignore
