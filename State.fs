module State
  type S<'State,'Value> =
    S of ('State -> 'Value * 'State)

  // encapsulate the function call that "runs" the state
  let runS (S f) state = f state

  let evalS (S f) state = f state |> fst

  let execS (S f) state = f state |> snd

  // lift a value to the S-world
  let returnS x =
    let run state =
        x, state
    S run

  // lift a monadic function to the S-world
  let bindS f xS =
    let run state =
        let x, newState = runS xS state
        runS (f x) newState
    S run

  type StateBuilder()=
    let (>>=) stateful binder = bindS binder stateful
    member this.Return(x) = returnS x
    member this.ReturnFrom(xS) = xS
    member this.Bind(xS,f) = bindS f xS
    member this.Zero() = returnS
    member this.Combine(statefulA, statefulB) =
        statefulA >>= (fun _ -> statefulB)
    member this.Delay(f) = f ()

  let state = new StateBuilder()

  let getS =
    let run state =
        // return the current state in the first element of the tuple
        state, state
    S run
  // val getS : S<State>

  let putS newState =
    let run _ =
        // return nothing in the first element of the tuple
        // return the newState in the second element of the tuple
        (), newState
    S run
  // val putS : 'State -> S<unit>
