// For more information see https://aka.ms/fsharp-console-apps
open System
open KVApp
open KVServer
open RaftNet.RaftConsole
open RaftLog
open RaftLogic
open Raft

runTests()
runLogTests()
runRaftLogicTests ()

let getNode () =
    Environment.GetCommandLineArgs() 
         |> fun args -> args.[1] |> int

let node = getNode ()

//console node 3
run node


