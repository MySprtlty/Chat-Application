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

        /*연결 중인 client들의 정보를 갖고있는 Dictionary*/
        private Dictionary<string, Socket> connectedClients = new();

        /*차단에 대한 정보를 갖고 있는 Dictionary*/
        private Dictionary<string, List<string>> muteTable = new();

        public Dictionary<string, Socket> ConnectedClients // Getter & Setter
        {
            get => connectedClients;
            set => connectedClients = value;
        }

        private Socket ServerSocket;

        private readonly IPEndPoint EndPoint = new(IPAddress.Parse("127.0.0.1"), 5001);

        /*멀티 스레드가 아니라 비동기라 race conditon 고려하지 않아도 된다.*/
        int clientNum; // 접속 중인 클라이언트 수
        //int seq; // 아이디 중복을 막기 위한 번호
        Server()
        {
            ServerSocket = new(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp
            );
            clientNum = 0;
            //seq = 1;
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
            
            string[] tokens = m.Split(':'); // 문자열을 ':' 구분자를 기준으로 분리하여 문자열 배열에 저장한다.
            string fromID; // 송신자
            string toID; // 수신자
            string code = tokens[0]; // ID or BR or TO

            Random rand = new Random();
            
            if (code.Equals("INIT")) // 클라이언트 처음 접속
            {
                clientNum++; // 처음 클라이언트가 접속되면, 자동으로 INIT:ID: 가 전송된다. 따라서 clientNum++;
                fromID = tokens[1].Trim(); // 문자열 시작과 끝의 공백을 제거한다.

                /*아이디 중복 처리*/
                if (ConnectedClients.ContainsKey(fromID))
                {
                    fromID = fromID + rand.Next(99999).ToString();
                    s.Send(Encoding.Unicode.GetBytes("ID_Changed:" + fromID + ":"));
                }

                Console.WriteLine("[접속{0}]ID:{1},{2}",
                    clientNum, fromID, s.RemoteEndPoint);
                
                connectedClients.Add(fromID, s); // Dictionary에 추가
                s.Send(Encoding.Unicode.GetBytes("ID_REG_Success:")); // 인코딩

                string greet = "Welcome " + fromID;
                Broadcast(s, greet);
            }
            else if(code.Equals("SEND")) // SEND Format
            {
                if (tokens[1].Equals("BR")) // 브로드 캐스트를 보낼 때 (SEND:BR:fromID:MSG:)
                {
                    fromID = tokens[2].Trim(); // 발신 아이디
                    string msg = tokens[3];
                    Console.WriteLine("[전체]{0}:{1}", fromID, msg);

                    string res = "[From:" + fromID + "]" + msg; // 받는 사람에게 보여줄 메시지
                    // Broadcast(s, res);
                    dandifiedBroadcast(fromID, s, res);
                    s.Send(Encoding.Unicode.GetBytes("BR_Success:"));
                }
                else if (tokens[1].Equals("UNI")) // 유니 캐스트를 보낼 때 (SEND:UNI:fromID:toID:MSG:)
                {
                    fromID = tokens[2].Trim(); // 발신 아이디
                    toID = tokens[3].Trim(); // 송신 아이디
                    string msg = tokens[4]; // 메세지
                    string rMsg = "[From:" + fromID + "][TO:" + toID + "]" + msg;
                    Console.WriteLine(rMsg);

                    string res = "[From:" + fromID + "]" + msg; // 받는 사람에게 보여줄 메시지
                    if (!isMuted(toID, fromID)) // 차단 여부 검사 (인자의 순서가 바뀌어야한다.)
                    {
                        SendTo(toID, res);
                        s.Send(Encoding.Unicode.GetBytes("UNI_Success:"));
                    }
                }
                else if (tokens[1].Equals("MUL")) // 멀티 캐스트를 보낼 때 (SEND:MUL:fromID:toID리스트:MSG:)
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
                            if (!isMuted(tokens[i], fromID)) // 차단 여부 검사
                            {
                                SendTo(tokens[i], res);
                            }
                            count++;
                        }
                    }
                    s.Send(Encoding.Unicode.GetBytes("MUL_Success: " + count.ToString()));
                }
            }
            else if(code.Equals("INFO")) // INFO format
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
            else if (code.Equals("SET"))
            {
                if(tokens[1].Equals("MUTE"))
                {
                    fromID = tokens[2].Trim();
                    toID = tokens[3].Trim();

                    if(!muteTable.ContainsKey(fromID)) // fromID가 아직 등록되어 있지 않다면
                    {
                        List<string> blackList = new List<string>();
                        blackList.Add(toID);
                        muteTable.Add(fromID, blackList);
                        s.Send(Encoding.Unicode.GetBytes("MUTE_Success:"));
                    }
                    else // fromID가 이미 등록되어 있다면
                    {
                        List<string> blackList;

                        muteTable.TryGetValue(fromID, out blackList!);

                        if (!blackList.Contains(toID)) // toID가 아직 등록되어 있지 않다면
                        {
                            blackList.Add(toID);
                            s.Send(Encoding.Unicode.GetBytes("MUTE_Success:"));
                        }
                        else
                        {
                            s.Send(Encoding.Unicode.GetBytes("MUTE_Already:"));
                        }
                    }
                    
                }
            }
            else if (code.Equals("FILE")) // 클라이언트가 파일을 전송할 때
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
                connectedClients.TryGetValue(id, out socket!); // out = ref
                // TryGetValue는 지정한 키와 연결된 값을 가져옵니다.
                // socket에 쓰여진다.
                try { socket.Send(bytes); } catch { } // toID에게 보낸다.
            }
        }
        void Broadcast(Socket s, string msg)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(msg);

            foreach (KeyValuePair<string, Socket> client in connectedClients.ToArray())
            {
                try
                {
                    if (s != client.Value) // 자기 자신은 제외
                    {
                        
                        client.Value.Send(bytes);
                    }
                }
                catch (Exception)
                {
                    Disconnected(client.Value);
                }
            }
        }

        /*muteTable를 확인하는 브로드 캐스트 메서드*/
        void dandifiedBroadcast(string fromID, Socket s, string msg)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(msg);

            foreach (KeyValuePair<string, Socket> client in connectedClients.ToArray())
            {
                try
                {
                    if (s != client.Value) // 자기 자신은 제외
                    {
                        if (!isMuted(client.Key, fromID))
                        {
                            client.Value.Send(bytes);
                        }
                    }
                }
                catch (Exception)
                {
                    Disconnected(client.Value);
                }
            }
        }

        /*차단 되었는지 유무를 확인하는 메서드*/
        bool isMuted(string fromID, string toID)
        {
            if(muteTable.ContainsKey(fromID)) // fromID가 있고
            {
                List<string> blackList;
                muteTable.TryGetValue(fromID, out blackList!);
                if(blackList.Contains(toID)) // toID가 있다면
                {
                    return true;
                }
            }
            return false;
        }


    }
}