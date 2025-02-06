#r "nuget: expecto"
#r "nuget: fsharpx.async"
#r "nuget: fspickler"
#r "nuget: fspickler.json"

#load "Raft/State.fs"
#load "Raft/Message.fs"
#load "Raft/RaftConfig.fs"
#load "Raft/RaftLog.fs"
#load "Raft/RaftLogic.fs"
#load "Raft/RaftNet.fs"
#load "Raft/Raft.fs"

open Raft.RaftNet
open Raft.RaftNet.RaftConsole
open Raft.Api


