#r "nuget: expecto"
#r "nuget: fsharpx.async"
#r "nuget: fspickler"
#r "nuget: fspickler.json"

#load "State.fs"
#load "Message.fs"
#load "RaftConfig.fs"
#load "RaftLog.fs"
#load "RaftLogic.fs"
#load "RaftNet.fs"
#load "Raft.fs"

open RaftNet
open RaftConsole
open Raft


