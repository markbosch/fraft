module KVApp

type Key = string
type Value = string

type KVData =
    {
        Get    : Key -> Value option
        Add    : Key -> Value -> unit
        Delete : Key -> unit
    }

type Msg =
    | Get of Key * AsyncReplyChannel<Value option>
    | Add of Key * Value
    | Delete of Key

let initialize () : KVData =
    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop state =
                async {
                    let! msg = inbox.Receive()
                    match msg with
                    | Get(key, reply) ->
                        let value = state |> Map.tryFind key
                        reply.Reply(value)
                        return! loop state
                    | Add(key, value) ->
                      let newState = state |> Map.add key value
                      return! loop newState
                    | Delete key ->
                      return! loop (state |> Map.remove key)
                }
            loop (Map.ofList [("key1", "value1")])
        )

    let get key =
      agent.PostAndReply (fun reply -> Get(key, reply))

    let add key value =
      agent.Post (Add(key, value))
    
    let delete key =
      agent.Post (Delete key)

    { 
      Get    = get 
      Add    = add
      Delete = delete
    }

let agent = initialize()
let handleMessage (msg : string) =
    let parts = msg.Split()
    match parts with
    | [|"get";key|] -> 
      let value = agent.Get(key)
      match value with
      | Some x -> $"ok {x}"
      | None -> "error notfound"
    | [|"set"; key; value;|] ->
      agent.Add key value
      "ok"
    | [|"delete"; key|] ->
      agent.Delete key
      "ok"
    | _ ->
      "bad command"

open Expecto

let tests  =
  testList "KVApp mailbox tests" [
    test "KVApp mailbox tests" {
      let result = handleMessage "set x 42"
      Expect.equal result "ok" "Expected result to be 'ok'"

      let result = handleMessage "get x"
      Expect.equal result "ok 42" "Expected result to be 'ok'"

      let result = handleMessage "get y"
      Expect.equal result "error notfound" "Expected result to be 'ok'"

      let result = handleMessage "delete x"
      Expect.equal result "ok" "Expected result to be 'ok'"

      let result = handleMessage "get x"
      Expect.equal result "error notfound" "Expected result to be 'ok'"

      let result = handleMessage "unknown command"
      Expect.equal result "bad command" "Expected result to be 'ok'"
    }
  ]

let runTests () =
  runTestsWithCLIArgs [] [||] tests |> ignore

  
  
