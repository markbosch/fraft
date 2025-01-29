// Traffic

module EventStore =
  type Projection<'State, 'Event> =
    {
      Init   : 'State
      Update : 'State -> 'Event -> 'State
    }

  let project (projection : Projection<_,_>) events =
    events
    |> List.fold projection.Update projection.Init 

  type EventProducer<'Event> =
    'Event list -> 'Event list

  type EventListener<'Event> =
    {
      Notify : 'Event list -> unit
    }

  type EventStore<'Event> =
    {
      Get      : unit -> 'Event list
      Append   : 'Event list -> unit
      Evolve   : EventProducer<'Event> -> unit
      OnEvents : IEvent<'Event list>
    }

  type Msg<'Event> =
    | Append of 'Event list
    | Get of AsyncReplyChannel<'Event list>
    | Evolve of EventProducer<'Event>

  let initialize () : EventStore<'Event> =
    // https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/members/events
    // Event is a 'notification handler'
    let eventsAppended = Event<'Event list>() // Can this be a normal function call?
    let agent =
      MailboxProcessor.Start(fun inbox ->
        let rec loop history =
          async {
            let! msg = inbox.Receive()
            match msg with
            | Append events ->
              eventsAppended.Trigger events
              
              return! loop (history @ events)
            
            | Get reply ->
              reply.Reply history

              return! loop history
            
            | Evolve eventProducer ->
              let newEvents =
                eventProducer history
              eventsAppended.Trigger newEvents
              
              return! loop (history @ newEvents)
          }
        
        loop []
      )
    
    let append events=
      agent.Post (Append events)

    let get () =
      agent.PostAndReply Get
    
    let evolve eventProducer =
      agent.Post (Evolve eventProducer)

    {
      Get      = get
      Append   = append
      Evolve   = evolve
      OnEvents = eventsAppended.Publish
    }


module TrafficLight =
  
  open EventStore

  // Current color and clock time
  type Clock = int

  type State =
    | NsGreen
    | NsGreenShort
    | NsYellow
    | EwGreen
    | EwGreenShort
    | EwYellow

  type TrafficLight = State * Clock

  type Event =
    | Clock
    | StateChanged of State
    | NsButton
    | EwButton

  let updateCurrentState (state : TrafficLight) event =
    match event with
    | Clock ->
        match state with
        | NsGreen, clock       -> NsGreen, clock + 1
        | NsGreenShort, clock  -> NsGreenShort, clock + 1
        | NsYellow, clock      -> NsYellow, clock + 1
        | EwGreen, clock       -> EwGreen, clock + 1
        | EwGreenShort, clock  -> EwGreenShort, clock + 1
        | EwYellow, clock      -> EwYellow, clock + 1

    | StateChanged state -> state, 0

    | NsButton ->
        match state with
        | EwGreen, clock -> EwGreenShort , clock
        | _ -> state 

    | EwButton ->
        match state with
        | NsGreen, clock -> NsGreenShort, clock
        | _ -> state

  /// Print the TrafficLight for debug
  let printTrafficLight (state : TrafficLight) =
    let (light, clock) = state
    printfn "TrafficLight(state=%A, clock=%d)" light clock

  let currentState : Projection<TrafficLight, Event> =
    {
      Init = NsGreen, 0
      Update = updateCurrentState
    }

  /// Event handling
  let clockTick events =
    let state =
        events
        |> project currentState

    let handleStateChanged predicate clock nextState =
        if predicate clock then
            [ StateChanged nextState ]
        else
            [ Clock ]

    match state with
    | NsGreen, clock ->
        handleStateChanged (fun c -> c = 60) clock NsYellow

    | NsGreenShort, value ->
        handleStateChanged (fun clock -> clock >= 15) value NsYellow

    | NsYellow, value ->
        handleStateChanged (fun clock -> clock = 5) value EwGreen

    | EwGreen, value ->
        handleStateChanged (fun clock -> clock = 30) value EwYellow

    | EwGreenShort, value ->
        handleStateChanged (fun clock -> clock >= 15) value EwYellow

    | EwYellow, value ->
        handleStateChanged (fun clock -> clock = 5) value NsGreen

  let nsButton events =
    [ NsButton ]

  let ewButton events =
    [ EwButton ]


module TrafficControl =
  open System
  open System.Net
  open System.Net.Sockets
  open System.Threading
  open System.Text

  open EventStore
  open TrafficLight

  /// Configuration
  let clock_Tick = TimeSpan.FromSeconds(1)
  let ns_light_address =
    IPEndPoint(IPAddress.Parse("127.0.0.1"), 17000)

  let ew_light_address =
    IPEndPoint(IPAddress.Parse("127.0.0.1"), 18000)

  let btn_address =
    IPEndPoint(IPAddress.Parse("127.0.0.1"), 20000)

  let b (input : string) =
    Encoding.UTF8.GetBytes(input)

  let output state =
    match state with
    | EwGreen ->
      (b"G", b"R")
    | EwGreenShort ->
      (b"G", b"R")
    | EwYellow ->
      (b"Y", b"R")
    | NsGreen ->
      (b"R", b"G")
    | NsGreenShort ->
      (b"R", b"G")
    | NsYellow ->
      (b"R", b"Y")

  let eventStore : EventStore<Event> = EventStore.initialize ()

  type Msg<'Event> =
  | Notify of 'Event list

  // Build the EventListener
  let initialize () : EventListener<_> =
    
    let socket = new Socket(
      AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    let agent =
      MailboxProcessor.Start(fun inbox ->
        let rec loop () =
          async {
            let! msg = inbox.Receive()

            match msg with
            | Notify _ ->
              // project the traffic light state
              // so that we can print it. Debug purposes
              let trafficLight =
                eventStore.Get ()
                |> project currentState
              
              printTrafficLight trafficLight

              // Deconstruct the traffic light. We only
              // need the state -> light color to get the
              // output we need to send over the wire.
              let (state, _) = trafficLight 
              let ew, ns = output state
              socket.SendTo(ew, ew_light_address) |> ignore
              socket.SendTo(ns, ns_light_address) |> ignore
              return! loop ()
          }
        
        loop ()
      )
    
    let notify events=
      agent.Post (Notify events)

    {
      Notify = notify
    }

  let runTimer = async {
      let rec loop () =
        Thread.Sleep clock_Tick 
        eventStore.Evolve clockTick
        loop ()
      
      loop ()
    }

  let watchButtons = async {
    let socket = new Socket(
      AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
    socket.Bind btn_address
    while true do
      let buffer: byte[] = Array.zeroCreate 1024
      let received = socket.Receive buffer
      let msg = Encoding.UTF8.GetString(buffer, 0, received)
      if msg = "EW" then
        eventStore.Evolve ewButton
      else if msg = "NS" then
        eventStore.Evolve nsButton
  }

  // Will use the EventStore as an "Event-Queue"
  // The EventListener will subscribe on the
  // events from the EventStore
  let eventListener = initialize () 
  eventStore.OnEvents.Add eventListener.Notify

  Async.Start(runTimer)
  Async.Start(watchButtons)

// todo:
// - project only the light state, instead of the whole
//   trafficlight. 
// - figure out why the append is not called / invoked
// - Write a blog post :P
System.Console.ReadLine() |> ignore
