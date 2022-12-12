using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Client
{
    public class Client
    {
        private readonly static int BufferSize = 4096;

        public static void Main()
        {
            try
            {
                new Client().Init();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            Console.WriteLine("Press any key to exit the program.");
            Console.ReadKey();
        }

        private Socket clientSocket;
        public Socket ClientSocket
        {
            get => clientSocket;
            set => clientSocket = value;
        }
        private readonly IPEndPoint EndPoint = new(IPAddress.Parse("127.0.0.1"), 5001);

        public Client()
        {
            ClientSocket = new(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp
            );
        }
        string nameID;
        void Init()
        {
            ClientSocket.Connect(EndPoint);
            Console.WriteLine($"Server connected.");

            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.Completed += new EventHandler<SocketAsyncEventArgs>(Received);
            ClientSocket.ReceiveAsync(args);

            Send();
        }

        void Received(object? sender, SocketAsyncEventArgs e)
        {
            try
            {
                byte[] data = new byte[BufferSize];
                Socket server = (Socket)sender!;
                int n = server.Receive(data);

                string str = Encoding.Unicode.GetString(data);
                str = str.Replace("\0", "");
                Console.WriteLine("수신:" + str);

                //if(str.Contains("ID_Changed"))
                //{
                //    string[] tokens = str.Split(':');
                //    nameID = tokens[1];
                //}

                MessageProc(server, str);

                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.Completed += new EventHandler<SocketAsyncEventArgs>(Received);
                ClientSocket.ReceiveAsync(args);
            }
            catch (Exception)
            {
                Console.WriteLine($"Server disconnected.");
                ClientSocket.Close();
            }
        }

        void Send()
        {
            byte[] dataID;
            Console.WriteLine("ID를 입력하세요");
            nameID = Console.ReadLine()!;
            
            string message = "INIT:" + nameID + ":";
            dataID = Encoding.Unicode.GetBytes(message);
            clientSocket.Send(dataID); // 처음 접속하면 INIT:아이디: 를 서버에게 보낸다.


            Console.WriteLine("<간단 매뉴얼>\n" + "특정 사용자에게 보낼 때는 UNI:아이디:메시지 로 입력하시고\n" +
                "브로드캐스트하려면 BR:메시지 를 입력하세요\n" +
                "멀티캐스트를하려면 MUL:아이디 리스트:메시지 를 입력하세요\n");
            do
            {
                byte[] data;
                string msg = Console.ReadLine()!;
                string[] tokens = msg.Split(':');
                string m;
                if (tokens[0].Equals("BR")) // Broadcast로 보낼 때
                { // BR:메시지

                    /*서버에게 보낼 형식으로 변환*/
                    m = "SEND:BR:" + nameID + ":" + tokens[1] + ":";

                    data = Encoding.Unicode.GetBytes(m);
                    Console.WriteLine("[Broadcast]: {0}", tokens[1]);
                    try { ClientSocket.Send(data); } catch { }
                }
                else if(tokens[0].Equals("UNI"))  // Unicast로 보낼 때
                { // UNI:아이디:메시지

                    /*서버에게 보낼 형식으로 변환*/
                    m = "SEND:UNI:" + nameID + ":" + tokens[1] + ":" + tokens[2] + ":";
                    data = Encoding.Unicode.GetBytes(m);
                    Console.WriteLine("[Unicast -> {0}]: {1}", tokens[1], tokens[2]);
                    try { ClientSocket.Send(data); } catch { }
                }
                else if (tokens[0].Equals("MUL")) // Multicast로 보낼 때
                { // MUL:아이디 리스트:메시지

                    /*서버에게 보낼 형식으로 변환*/
                    m = "SEND:MUL:" + nameID + ":";
                    for (int i = 1; i < tokens.Length - 1; i++)
                    {
                        m += tokens[i].Trim() + ":";
                    }
                    m += tokens[^1] + ":";

                    data = Encoding.Unicode.GetBytes(m);
                    Console.WriteLine("[Multicast]: {0}", tokens[^1]);
                    try { ClientSocket.Send(data); } catch { }

                }
                else if (tokens[0].Equals("WHO")) // WHO
                {
                    m = "INFO:WHO:" + nameID + ":";
                    data = Encoding.Unicode.GetBytes(m);
                    Console.WriteLine("접속자 명단 요청");
                    try { ClientSocket.Send(data); } catch { }
                }
                else if(tokens[0].Equals("WC")) // WC
                {
                    m = "INFO:WC:" + nameID + ":";
                    data = Encoding.Unicode.GetBytes(m);
                    Console.WriteLine("접속자 수 요청");
                    try { ClientSocket.Send(data); } catch { }
                }
                else if(tokens[0].Equals("MUTE")) // MUTE:toID
                {
                    m = "SET:MUTE:" + nameID + ":" + tokens[1] + ":";
                    data = Encoding.Unicode.GetBytes(m);
                    Console.WriteLine("사용자 차단");
                    try { ClientSocket.Send(data); } catch { }
                }
                else if (tokens[0].Equals("File")) // 파일을 보낼 때
                {
                    SendFile(tokens[1]); // Buffer가 4096이다.
                    // file은 try-catch가 불필요하다.
                }
                else
                {
                    Debug.Fail("유효하지 않은 명령어");
                }
            } while (true);
        }
        void SendFile(string fileName)
        {
            // 보낼 파일의 사이즈가 얼마인지 알려줘야 한다.
            FileInfo fi = new FileInfo(fileName); // 파일에 대한 정보를 갖고 있는 Class
            string fileLength = fi.Length.ToString();
            byte[] bDts = Encoding.Unicode.GetBytes 
                ("File:" + fileName + ":" + fileLength + ":"); // 수신자가 fileName으로 미리 파일을 생성하고, 사이즈를 정할 수 있도록
                                                               // 파일의 사전정보를 보낸다.
            clientSocket.Send(bDts); // 사전 정보 Send

            byte[] bDtsRx = new byte[4096];
            FileStream fs = new FileStream(fileName,
                FileMode.Open, FileAccess.Read,
                FileShare.None);

            long received = 0;
            while(received < fi.Length)
            {
                received += fs.Read(bDtsRx, 0, 4096);
                clientSocket.Send(bDtsRx);
                Array.Clear(bDtsRx);
            }
            fs.Close();

            Console.WriteLine("파일 송신 종료");

        }

        void MessageProc(Socket s, string str)
        {

            /*서버에서 닉네임을 바꾸라는 지시를 내린경우*/
            if (str.Contains("ID_Changed"))
            {
                string[] tokens = str.Split(':');
                nameID = tokens[1];
            }
        }
    }
}
