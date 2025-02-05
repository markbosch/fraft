module RaftConfig

    open System.Net

    let kvservers = Map [
      (0, IPEndPoint(0, 12345));
      (1, IPEndPoint(0, 12346));
      (2, IPEndPoint(0, 12347));
      //(3, IPEndPoint(0, 12348));
      //(4, IPEndPoint(0, 12349));
    ]
    
    let servers = Map [
      (0, IPEndPoint(0, 15000));
      (1, IPEndPoint(0, 16000));
      (2, IPEndPoint(0, 17000));
      //(3, IPEndPoint(0, 18000));
      //(4, IPEndPoint(0, 19000));
    ]

    let getAddress dest =
      servers |> Map.find dest

    let HEARTBEAT_TIMER = 1
    let MAX_ENTRIES = 1000
    let ELECTION_TIMEOUT = 5   // In seconds
    let ELECTION_RANDOM = 3    // In seconds
