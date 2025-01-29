module KVApp
  // todo: revisit the state monad
  //       https://fsharpforfunandprofit.com/posts/monadster-3/
  module State =
    type State<'S, 'A> =
      State of ('S -> 'A * 'S)

    let run (State f) state =
      f state

    let ret result =
      let run state =
        result, state
      State run

    let bind binder stateful =
      let run state =
        let x, newState = run stateful state
        run (binder x) newState
      State run

  open State

  type StatefulBuilder() =
    let (>>=) stateful binder        = State.bind binder stateful
    member __.Return(result)         = State.ret result
    member __.ReturnFrom(stateful)   = stateful
    member __.Bind(stateful, binder) = stateful >>= binder
    member __.Zero()                 = State.ret ()
    member __.Combine(statefulA, statefulB) =
        statefulA >>= (fun _ -> statefulB)
    member __.Delay(f)               = f ()

  let state = StatefulBuilder()

  let get key : State<Map<string,string>, string option> =
    State.State (fun map ->
      let value = map |> Map.tryFind key
      value, map)

  let add key value : State<Map<string,string>,unit> =
    State.State (fun map ->
      let newMap = map |> Map.add key value
      (), newMap)

  let delete key : State<Map<string,string>,unit> =
    State.State (fun map ->
      let newMap = map |> Map.remove key
      (), newMap)

  let HandleMessage (msg : string) =
    state {
      let parts = msg.Split()
      match parts with
      | [|"get"; key|] ->
        let! value = get key
        match value with
        | Some x -> return $"ok {x}"
        | None   -> return "error notfound"
      | [|"set"; key; value;|] ->
        do! add key value
        return "ok"
      | [|"delete"; key|] ->
        do! delete key
        return "ok"
      | _ ->
        return "bad command"
    }

  open Expecto

  let tests =
    testList "KVApp tests" [
      test "KVApp tests" {
        let init = Map.empty<string,string>
        let result, state = State.run (HandleMessage "set x 42") init
        Expect.equal result "ok" "Expected result to be 'ok'"
        let result2, state2 = State.run (HandleMessage "get x") state
        Expect.equal result2 "ok 42" "Expected result to be 'ok 42'"
        let result3, state3 = State.run (HandleMessage "get y") state2
        Expect.equal result3 "error notfound" "Expected result to be 'error notfound'"
        let result4, state4 = State.run (HandleMessage "delete x") state3
        Expect.equal result4 "ok" "Expected result to be 'ok'"
        let result5, _ = State.run (HandleMessage "bad x y z") state4
        Expect.equal result5 "bad command" "Expected result to be 'bad command'"
      }
    ]

  let runTests () =
    runTestsWithCLIArgs [] [||] tests |> ignore
