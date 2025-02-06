// For more information see https://aka.ms/fsharp-console-apps
open System
open KVApp
open KVServer

let getNode () =
    Environment.GetCommandLineArgs() 
         |> fun args -> args.[1] |> int

let node = getNode ()

run node


