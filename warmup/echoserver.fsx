open System.Net
open System.Net.Sockets

open System.Globalization
open System.Text

let socket = new Socket(SocketType.Stream, ProtocolType.Tcp)

socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345))
socket.Listen()
let client = socket.Accept()
let msg = System.DateTime.Now.ToString(CultureInfo.InvariantCulture)
client.Send(Encoding.UTF8.GetBytes(msg))
client.Close()