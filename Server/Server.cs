using System.Net.Sockets;
using System.Net;
using System.Text;

namespace Server
{
    public class Server
    {
        private readonly static int BufferSize = 4096;

        public static void Main()
        {
            try
            {
                new Server().Init();
            }
            catch (Exception e) // 에러가 Throw 됐을때
            {
                Console.WriteLine(e.Message);
            }
        }

        private Dictionary<string, Socket> connectedClients = new();

        public Dictionary<string, Socket> ConnectedClients // Getter & Setter
        {
            get => connectedClients;
            set => connectedClients = value;
        }

        private Socket ServerSocket;

        private readonly IPEndPoint EndPoint = new(IPAddress.Parse("127.0.0.1"), 5001);

        int clientNum; // 공유 자원 (race condition 발생 가능, 기말 프젝때는 Thread-Safe하게 설계하기)
        Server()
        {
            ServerSocket = new(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp
            );
            clientNum = 0;
        }

        void Init()
        {
            ServerSocket.Bind(EndPoint);
            ServerSocket.Listen(100);
            Console.WriteLine("Waiting connection request.");

            //Accept();
            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.Completed += new EventHandler<SocketAsyncEventArgs>(Accept);
            ServerSocket.AcceptAsync(args);

            SendNotice();
        }

        void Accept(object? sender, SocketAsyncEventArgs e)
        {
            Socket client;

            try
            {
                client = e.AcceptSocket!;

                SocketAsyncEventArgs argsA = new SocketAsyncEventArgs();
                argsA.Completed += new EventHandler<SocketAsyncEventArgs>(Accept);
                ServerSocket.AcceptAsync(argsA);

                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.Completed += new EventHandler<SocketAsyncEventArgs>(Received);
                client.ReceiveAsync(args);

            }
            catch { }

            
        }
        void SendNotice()
        {
            do
            {
                string msg = Console.ReadLine()!;
                msg = "Server:" + msg + ":";
                Console.WriteLine(msg);
                try
                {
                    Broadcast(ServerSocket, msg);
                }
                catch { }
            } while (true);
        }
        void Disconnected(Socket client)
        {
            Console.WriteLine($"Client disconnected: {client.RemoteEndPoint}.");
            foreach (KeyValuePair<string, Socket> clients in connectedClients)
            {
                if (clients.Value == client)
                {
                    ConnectedClients.Remove(clients.Key);
                    clientNum--;
                }
            }
            client.Disconnect(false); // 재연결 안함
            client.Close();
        }

        void Received(object? sender, SocketAsyncEventArgs e)
        {
            Socket client = (Socket)sender!;
            byte[] data = new byte[BufferSize];
            try
            {
                int n = client.Receive(data);
                if (n > 0) // 연결중, 상대방의 종료를 알 수 있는 부분
                {

                    MessageProc(client, data); // 메세지 처리를 담당하는 함수 호출

                    SocketAsyncEventArgs argsR = new SocketAsyncEventArgs();
                    argsR.Completed += new EventHandler<SocketAsyncEventArgs>(Received); // 이벤트 핸들러 등록
                    // +=는 operator overloading (add)
                    client.ReceiveAsync(argsR);
                }
                else { throw new Exception(); }
            }
            catch (Exception)
            {
                Disconnected(client);
            }
        }

        /*Message Process, 메세지를 유형별로 처리하는 메서드*/
        void MessageProc(Socket s, byte[] bytes) // s는 현재 클라이언트 소켓값
        {
            string m = Encoding.Unicode.GetString(bytes); // Decoding
            //
            string[] tokens = m.Split(':'); // 문자열을 ':' 구분자를 기준으로 분리하여 문자열 배열에 저장한다.
            string fromID; // 송신자
            string toID; // 수신자
            string code = tokens[0]; // ID or BR or TO


            if (code.Equals("INIT")) // 클라이언트 처음 접속
            {
                clientNum++; // 처음 클라이언트가 접속되면, 자동으로 ID: 가 전송된다. 따라서 clientNum++;
                fromID = tokens[1].Trim(); // 문자열 시작과 끝의 공백을 제거한다.
                // 
                Console.WriteLine("[접속{0}]ID:{1},{2}",
                    clientNum, fromID, s.RemoteEndPoint);
                //
                connectedClients.Add(fromID, s); // Dictionary
                s.Send(Encoding.Unicode.GetBytes("ID_REG_Success:")); // 인코딩
                Broadcast(s, m);
            }
            else if(code.Equals("SEND"))
            {
                if (tokens[1].Equals("BR")) // 브로드 캐스트를 보낼 때 (BR:발신아이디:메세지:)
                {
                    fromID = tokens[1].Trim(); // 발신 아이디
                    string msg = tokens[2];
                    Console.WriteLine("[전체]{0}:{1}", fromID, msg);
                    //
                    Broadcast(s, m);
                    s.Send(Encoding.Unicode.GetBytes("BR_Success:"));
                }
                else if (tokens[1].Equals("UNI")) // 유니 캐스트를 보낼 때 (TO:발신:송신:메시지:)
                {
                    fromID = tokens[2].Trim(); // 발신 아이디
                    toID = tokens[3].Trim(); // 송신 아이디
                    string msg = tokens[4]; // 메세지
                    string rMsg = "[From:" + fromID + "][TO:" + toID + "]" + msg;
                    Console.WriteLine(rMsg);

                    string res = "[From:" + fromID + "]" + msg; // 받는 사람에게 보여줄 메시지
                    SendTo(toID, res);
                    s.Send(Encoding.Unicode.GetBytes("UNI_Success:"));
                }
                else if (tokens[1].Equals("MUL"))
                {
                    Console.WriteLine(m);
                    fromID = tokens[2].Trim();
                    string msg = tokens[^2];
                    int count = 0;
                    string res = "[From:" + fromID + "]" + msg; // 받는 사람에게 보여줄 메시지
                    for (int i = 3; i < tokens.Length - 1; i++)
                    {                       
                        if (connectedClients.ContainsKey(tokens[i])) // 만약 유효한 아이디라면
                        {
                            SendTo(tokens[i], res);       
                            count++;
                        }
                    }
                    s.Send(Encoding.Unicode.GetBytes("MUL_Success: " + count.ToString()));
                }
            }
            else if(code.Equals("INFO"))
            {
                if (tokens[1].Equals("WHO"))
                {
                    fromID = tokens[2].Trim(); // 발신 아이디
                    string res = "Client List: "; // 응답 문자열
                    foreach (KeyValuePair<string, Socket> client in connectedClients.ToArray())
                    {
                        res += client.Key;
                        res += " ";
                    }
                    res += "\n";

                    SendTo(fromID, res);
                    s.Send(Encoding.Unicode.GetBytes("WHO_Success:"));
                }
                else if (tokens[1].Equals("WC"))
                {
                    fromID = tokens[2].Trim(); // 발신 아이디
                    string res = "Client Num: " + clientNum.ToString()+"명\n"; // 응답 문자열

                    SendTo(fromID, res);
                    s.Send(Encoding.Unicode.GetBytes("WC_Success:"));
                }
            }
            else if (code.Equals("File")) // 클라이언트가 파일을 전송할 때
            { 
                ReceiveFile(s, m); // 파일 수신 처리를 맡는 함수
            }
            else
            {
                Broadcast(s, m);
            }
        }
        void ReceiveFile(Socket s, string m)
        {
            string output_path = @"FileDown\";
            if (!Directory.Exists(output_path)) // 디렉터리가 존재하지 않으면
            {
                Directory.CreateDirectory(output_path);        
            }
            string[] tokens = m.Split(':');
            string fileName = tokens[1].Trim(); // 파일명
            long fileLength = Convert.ToInt64(tokens[2].Trim()); // 파일 길이 
            string FileDest = output_path + fileName; // 경로 Join

            long fLen = 0;
            FileStream fs = new FileStream(FileDest, 
                FileMode.OpenOrCreate,
                FileAccess.Write, 
                FileShare.None);

            while(fLen < fileLength)
            {
                byte[] fData = new byte[4096];
                int r = s.Receive(fData, 0, 4096, SocketFlags.None);
                fs.Write(fData, 0, r);
                fLen += r;
            }
            fs.Close();

        }

        void SendTo(string id, string msg) // 전달해주는 메서드
        {
            Socket socket;
            byte[] bytes = Encoding.Unicode.GetBytes(msg);
            if (connectedClients.ContainsKey(id)) // Dictionary<TKey,TValue>에 지정한 키가 포함되어 있는지 여부를 확인합니다
            {
                //
                connectedClients.TryGetValue(id, out socket!); // out = ref
                // TryGetValue는 지정한 키와 연결된 값을 가져옵니다.
                // socket에 쓰여진다.
                try { socket.Send(bytes); } catch { } // toID에게 보낸다.
            }
        }
        void Broadcast(Socket s, string msg) // 5-2ㅡ모든 클라이언트에게 Send
        {
            byte[] bytes = Encoding.Unicode.GetBytes(msg);
            //
            foreach (KeyValuePair<string, Socket> client in connectedClients.ToArray())
            {
                try
                {
                    //5-2 send
                    //
                    if (s != client.Value) // 자기 자신은 제외
                        client.Value.Send(bytes);

                }
                catch (Exception)
                {
                    Disconnected(client.Value);
                }
            }
        }

    }
}