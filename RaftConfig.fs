module RaftConfig

    open System.Net
    
    let servers = Map [
      (0, IPEndPoint(0, 15000));
      (1, IPEndPoint(0, 16000));
      (2, IPEndPoint(0, 17000));
      //(3, IPEndPoint(0, 18000));
      //(4, IPEndPoint(0, 19000));
    ]

    let getAddress dest =
      servers |> Map.find dest

    let HEARTBEAT_TIMER = 10
    let MAX_ENTRIES = 1000
