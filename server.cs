using System.Data.SQLite;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

/*
 *  Class designed to decode and hold all attributes contained in an WebSocket message.
 *  WebSocket message is passed to the constructor which then decodes it and stores the attributes in the WSMessage object
 */
public class WSMessage {
    private Byte[] encodedmsg; //Contains the encoded message
    private Byte[] PAYLOAD; //Contains the decoded payload

    public Boolean was_handshake = false; //was the incoming data just a handshake

    public Boolean FIN; //has the full message been sent or just a frame
    public Boolean RSV1;
    public Boolean RSV2;
    public Boolean RSV3;
    public Byte OPCODE; //type of message. 0x1 -> text message
    public Boolean MASKED;
    public uint PAYLOADLENGTH = 0; //max int16_t, table can't be longer that that
    public Byte[] MASK;

    /*          https://www.rfc-editor.org/rfc/rfc6455
          0                   1                   2                   3
      0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
     +-+-+-+-+-------+-+-------------+-------------------------------+
     |F|R|R|R| opcode|M| Payload len |    Extended payload length    |
     |I|S|S|S|  (4)  |A|     (7)     |             (16/64)           |
     |N|V|V|V|       |S|             |   (if payload len==126/127)   |
     | |1|2|3|       |K|             |                               |
     +-+-+-+-+-------+-+-------------+ - - - - - - - - - - - - - - - +
     |     Extended payload length continued, if payload len == 127  |
     + - - - - - - - - - - - - - - - +-------------------------------+
     |                               |Masking-key, if MASK set to 1  |
     +-------------------------------+-------------------------------+
     | Masking-key(continued)       |          Payload Data          |
     +-------------------------------- - - - - - - - - - - - - - - - +
     :                     Payload Data continued...                 :
     + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
     |                     Payload Data continued...                 |
     +---------------------------------------------------------------+

    */

    public WSMessage() {

    }

    /*
     *  Decodes an WebSockets message and stores the contents in WSMessage attributes
     */
    public Byte[] Decode(Byte[] msg) {
        try {
            this.encodedmsg = new byte[msg.Length];
            Buffer.BlockCopy(msg, 0, this.encodedmsg, 0, msg.Length); //copy the undecoded WSMessage for later use
            uint byteindex = 0; //keeps track of which byte we are reading

            //1st byte -> FIN(1b) RSV1(1b) RSV2(1b) RSV3(1b) OPCODE(4b)
            Byte b = msg[byteindex];
            this.FIN = (b & (1 << 7)) != 0;
            this.RSV1 = (b & (1 << 6)) != 0;
            this.RSV2 = (b & (1 << 5)) != 0;
            this.RSV3 = (b & (1 << 4)) != 0;
            this.OPCODE = (byte)(b & 0x0F);
            byteindex++;

            //2nd byte -> MASKED(1b) PAYLOADLEN(7b)
            //If payload length is <= 125 then it's the real payload length
            //If it's 126 -> length is stored in the next 2 bytes
            //If it's 127 -> length is stored in the next 8 bytes
            b = msg[byteindex];
            this.MASKED = (b & (1 << 7)) != 0;
            this.PAYLOADLENGTH = (byte)(b & 0x7F);
            byteindex++;
            if (this.PAYLOADLENGTH == 126) {
                Byte[] lenbytes = new Byte[2];
                lenbytes[1] = msg[2];
                lenbytes[0] = msg[3];
                this.PAYLOADLENGTH = BitConverter.ToUInt16(lenbytes, 0);
                byteindex += 2;
            }
            else if (this.PAYLOADLENGTH == 127) {
                Byte[] lenbytes = new Byte[8];
                for (int i = 7; i >= 0; i--)
                    lenbytes[i] = msg[byteindex + (7 - i)];
                ulong payloadlentmp = BitConverter.ToUInt64(lenbytes, 0);
                this.PAYLOADLENGTH = (uint)payloadlentmp;
                byteindex += 8;
            }

            //if MASKED == 1 then next 4 bytes are mask bytes
            if (this.MASKED) {
                this.MASK = new Byte[4];
                for (int i = 0; i < 4; i++) {
                    this.MASK[i] = msg[byteindex + i];
                }
                byteindex += 4;
            }

            //we decode the payload
            this.PAYLOAD = new byte[this.PAYLOADLENGTH];
            for (uint i = byteindex; i < byteindex + this.PAYLOADLENGTH; i++) {
                byte decodedval;

                if (this.MASKED)
                    decodedval = (byte)(msg[i] ^ this.MASK[(i - byteindex) % 4]);
                else
                    decodedval = msg[i];

                this.PAYLOAD[i - byteindex] = decodedval;
            }

            return this.PAYLOAD;

        }
        catch (Exception e) {
            Console.WriteLine(e.ToString());
            return null;
        }
    }

    /*
     *  Encodes the given message WITHOUT using mask and stores the contents in WSMessage attributes
     */
    public Byte[] EncodeNoMask(Byte[] msg) {
        try {
            this.PAYLOAD = new Byte[msg.Length];
            Buffer.BlockCopy(msg, 0, this.PAYLOAD, 0, msg.Length); //copy so that we have the original undecoded message stored

            int startindex = 0; //index from which the payload begins

            //1st byte -> FIN(1b) RSV1(1b) RSV2(1b) RSV3(1b) OPCODE(4b)
            this.FIN = true;
            this.RSV1 = false;
            this.RSV2 = false;
            this.RSV3 = false;
            this.OPCODE = 0x1;
            byte b1 = 0x81; //10000001

            //2nd byte -> MASKED(1b) PAYLOADLEN(7b)
            //If payload length is <= 125 then it's the real payload length
            //If it's 126 -> length is stored in the next 2 bytes
            //If it's 127 -> length is stored in the next 8 bytes <- can't happen, too large for the msg[] array
            this.MASKED = false;
            this.PAYLOADLENGTH = (uint)msg.Length;
            byte b2 = 0x0; //00000000
            if (this.PAYLOADLENGTH <= 125) {
                b2 = (byte)(b2 | this.PAYLOADLENGTH);
                this.encodedmsg = new Byte[2 + this.PAYLOADLENGTH]; //at this point we know the size of the undecoded message
                this.encodedmsg[0] = b1;
                this.encodedmsg[1] = b2;
                startindex = 2;
            }
            else if (this.PAYLOADLENGTH > 125 && this.PAYLOADLENGTH <= UInt16.MaxValue) {
                //length is stored in the next 2 bytes
                b2 = (byte)(b2 | (uint)126);
                this.encodedmsg = new Byte[4 + this.PAYLOADLENGTH]; //at this point we know the size of the undecoded message
                byte b3 = (byte)(this.PAYLOADLENGTH >> 8);
                byte b4 = (byte)(this.PAYLOADLENGTH & 0xFF);
                this.encodedmsg[0] = b1;
                this.encodedmsg[1] = b2;
                this.encodedmsg[2] = b3;
                this.encodedmsg[3] = b4;
                startindex = 4;
            }
            else if (this.PAYLOADLENGTH <= int.MaxValue) {
                //length is stored in the next 8 bytes
                this.encodedmsg = new Byte[10 + this.PAYLOADLENGTH];
                byte b3, b4, b5, b6, b7, b8, b9, b10;
                b3 = (byte)(this.PAYLOADLENGTH >> 56);
                b4 = (byte)(this.PAYLOADLENGTH >> 48);
                b5 = (byte)(this.PAYLOADLENGTH >> 40);
                b6 = (byte)(this.PAYLOADLENGTH >> 32);
                b7 = (byte)(this.PAYLOADLENGTH >> 24);
                b8 = (byte)(this.PAYLOADLENGTH >> 16);
                b9 = (byte)(this.PAYLOADLENGTH >> 8);
                b10 = (byte)(this.PAYLOADLENGTH & 0xFF);

                this.encodedmsg[0] = b1;
                this.encodedmsg[1] = b2;
                this.encodedmsg[2] = b3;
                this.encodedmsg[3] = b4;
                this.encodedmsg[4] = b5;
                this.encodedmsg[5] = b6;
                this.encodedmsg[6] = b7;
                this.encodedmsg[7] = b8;
                this.encodedmsg[8] = b9;
                this.encodedmsg[9] = b10;

                startindex = 10;
            }
            else {
                return null;
            }

            /*
             *  Insert the payload
             */                                                   //safe cast, we checked before if PAYLOADLENGTH is <= int.MaxValue
            Buffer.BlockCopy(msg, 0, this.encodedmsg, startindex, (int)this.PAYLOADLENGTH);
            return this.encodedmsg;
        }
        catch (Exception e) {
            Console.WriteLine(e.ToString());
            return null;
        }
    }

    /*
     *  Encodes the given message WITH mask and stores the contents in WSMessage attributes
     *  ? Server shouldn't mask its messages ? Could be implemented if client would support it. Not sure if there's a need for that though.
     */
    //public Boolean EncodeWithMask(Byte[] msg) {
    //}

    /*
     *  Generates a PING message -> OPCODE = 0x9
     */
    public Byte[] GeneratePingMessage() {
        this.encodedmsg = new Byte[2];

        //1st byte -> FIN(1b) RSV1(1b) RSV2(1b) RSV3(1b) OPCODE(4b)
        this.FIN = true;
        this.RSV1 = false;
        this.RSV2 = false;
        this.RSV3 = false;
        this.OPCODE = 0x9;
        byte b1 = 0x89; //10001001

        //2nd byte -> MASKED(1b) PAYLOADLEN(7b)
        this.MASKED = false;
        this.PAYLOADLENGTH = 0;
        byte b2 = 0x0; //00000000

        this.encodedmsg[0] = b1;
        this.encodedmsg[1] = b2;

        return this.encodedmsg;
    }

    /*
     *  Generates a PONG message -> OPCODE = 0xA
     */
    public Byte[] GeneratePongMessage() {
        this.encodedmsg = new byte[2];
        //1st byte -> FIN(1b) RSV1(1b) RSV2(1b) RSV3(1b) OPCODE(4b)
        this.FIN = true;
        this.RSV1 = false;
        this.RSV2 = false;
        this.RSV3 = false;
        this.OPCODE = 0xA;
        byte b1 = 0x8A; //10001010

        //2nd byte -> MASKED(1b) PAYLOADLEN(7b)
        this.MASKED = false;
        this.PAYLOADLENGTH = 0;
        byte b2 = 0x0; //00000000

        this.encodedmsg[0] = b1;
        this.encodedmsg[1] = b2;

        return this.encodedmsg;
    }

    /*
     *  Generates a CLOSE message -> OPCODE = 0x8
     */
    public Byte[] GenerateCloseMessage() {
        this.encodedmsg = new byte[2];
        //1st byte -> FIN(1b) RSV1(1b) RSV2(1b) RSV3(1b) OPCODE(4b)
        this.FIN = true;
        this.RSV1 = false;
        this.RSV2 = false;
        this.RSV3 = false;
        this.OPCODE = 0xA;
        byte b1 = 0x88; //10001000

        //2nd byte -> MASKED(1b) PAYLOADLEN(7b)
        this.MASKED = false;
        this.PAYLOADLENGTH = 0;
        byte b2 = 0x0; //00000000

        this.encodedmsg[0] = b1;
        this.encodedmsg[1] = b2;

        return this.encodedmsg;
    }

    public Boolean IsTextMessage() {
        if (this.OPCODE == 0x1)
            return true;
        return false;
    }

    public Boolean IsPingMessage() {
        if (this.OPCODE == 0x9)
            return true;
        return false;
    }

    public Boolean IsPongMessage() {
        if (this.OPCODE == 0xA)
            return true;
        return false;
    }

    public Boolean IsCloseMessage() {
        if (this.OPCODE == 0x8)
            return true;
        return false;
    }

    public Boolean IsHandshake() {
        return this.was_handshake;
    }

    /*
     *  Returns the decoded message as a String
     */
    public String GetDecodedMessage() {
        return Encoding.ASCII.GetString(this.PAYLOAD);
    }

    /*
     *  Returns the encoded message as a Byte[]
     */
    public Byte[] GetEncodedMessage() {
        return this.encodedmsg;
    }

    /*
     *  Composes an response for the HTTP WebSocket handshake
     *  Specification: developer.mozilla.org
     */
    public static Byte[] ComposeHandshakeResponse(String msg) {
        const String eol = "\r\n"; // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
        byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols" + eol
            + "Connection: Upgrade" + eol
            + "Upgrade: websocket" + eol
            + "Sec-WebSocket-Accept: " + Convert.ToBase64String(
                System.Security.Cryptography.SHA1.Create().ComputeHash(
                    Encoding.UTF8.GetBytes(
                        new System.Text.RegularExpressions.Regex("Sec-WebSocket-Key: (.*)").Match(msg).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                    )
                )
            ) + eol
            + eol);

        return response;
    }

}

public class Server {
    static String DBPath = @"C:\Users\user\Documents\VS projekti\WebSockets Server\gpsdb.db"; //SQLite database location

    public static void Main(string[] args) {
        Console.WriteLine("TCP server starting.");

        //Check if the DB file exists
        if (!System.IO.File.Exists(DBPath)) {
            Console.WriteLine("Database file doesn't exist.");
            return;
        }

        try {
            //Start listening for TCP requests
            TcpListener server = new TcpListener(IPAddress.Parse("127.0.0.1"), 80);
            server.Start();
            Console.WriteLine("Server is up. Listening for incoming requests.");
            Console.WriteLine();

            //Constantly check if there are any requests pending
            while (true) {
                //Request is pending
                if (server.Pending()) {
                    Console.WriteLine("Accepting request.");
                    Console.WriteLine();

                    try {
                        //Spawn a new task
                        TcpClient client = server.AcceptTcpClientAsync().Result;
                        Task t = new Task(() => { ClientHandler handler = new ClientHandler(client, DBPath); });
                        t.Start();
                    }
                    catch (Exception e) {
                        Console.Write("Error while spawning a new task: ");
                        Console.WriteLine(e.ToString());
                    }
                }
                Thread.Sleep(1);
            }
        }
        catch (Exception e) {
            Console.WriteLine();
            Console.Write("Error: ");
            Console.WriteLine(e.Message);
        }
    }

    /*
     *  Class designed for communicating with client using simple send and receive functions
     */
    private class ClientHandler {
        private NetworkStream stream;
        private Boolean handshake_done = false; //marks if the WebSocket handshake has been done already
        private String DBPath = "";

        public ClientHandler(TcpClient client, String DBPath) {
            this.stream = client.GetStream();
            this.DBPath = DBPath;

            //Constantly check for new messages
            while (true) {
                Thread.Sleep(1);

                if (client.Available == 0)
                    continue;

                WSMessage inputwsmsg = getMessage(client.Available);
                if (inputwsmsg == null)
                    continue;

                //It is a HANDSHAKE message
                if (inputwsmsg.IsHandshake()) {
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}: Handshaking done");
                    Console.WriteLine();
                    continue;
                }

                //It is a TEXT message
                if (inputwsmsg.IsTextMessage()) {
                    //Extract the decoded payload
                    String received_data = inputwsmsg.GetDecodedMessage();
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}: Received: {received_data}");

                    //Check if it's properly formatted (eg. not an SQL injection)
                    if (!VerifyData(received_data)) {
                        client.Close();
                        return;
                    }

                    //Extract the GPS data from the payload
                    String[] data_array = received_data.Split(',');
                    uint device_id = UInt32.Parse(data_array[0]);
                    uint timestamp = UInt32.Parse(data_array[1]);
                    double lat = Double.Parse(data_array[2]);
                    double lon = Double.Parse(data_array[3]);

                    //Store it in the database
                    try {
                        SQLiteConnection conn = new SQLiteConnection($"Data source={this.DBPath}");
                        conn.Open();
                        String query = $"INSERT INTO GPSDATA (device_id, timestamp, lat, lon) VALUES ({device_id}, {timestamp}, {lat}, {lon})";
                        SQLiteCommand command = new SQLiteCommand(query, conn);
                        command.ExecuteNonQuery();
                        conn.Close();
                        command.Dispose();
                        conn.Dispose();
                    }
                    catch (Exception e) {
                        Console.WriteLine(e.Message);
                        client.Close();
                        return;
                    }

                    //Reply
                    String reply_data = "OK";
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}: Replying: {reply_data}");
                    WSMessage outputwsmsg = new WSMessage();
                    outputwsmsg.EncodeNoMask(Encoding.ASCII.GetBytes(reply_data));
                    sendMessage(outputwsmsg);

                    Console.WriteLine();
                    continue;
                }

                //It is a PING message
                if (inputwsmsg.IsPingMessage()) {
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}: Received a PING message, sending PONG");
                    Console.WriteLine();

                    //send a PONG message
                    WSMessage outputwsmsg = new WSMessage();
                    outputwsmsg.GeneratePongMessage();
                    sendMessage(outputwsmsg);
                    continue;
                }

                //It is a PONG message
                if (inputwsmsg.IsPongMessage()) {
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}: Received a PONG message");
                    Console.WriteLine();
                    continue;
                }

                //It is a CLOSE message
                if (inputwsmsg.IsCloseMessage()) {
                    Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}: Closing connection");
                    Console.WriteLine();
                    client.Close();
                    return;
                }
            }
        }

        /*
         *  Decodes the incoming message and returns it as an WSMessage object
         */
        private WSMessage getMessage(int messagesize) {
            //Buffer for the incoming data
            Byte[] buffer = new Byte[messagesize];
            stream.Read(buffer, 0, buffer.Length);

            //WSMessage object that'll be returned
            WSMessage wsmsg = new WSMessage();

            //Check if the client is trying to handshake
            if (!handshake_done) {
                String msg = Encoding.ASCII.GetString(buffer);
                if (Regex.IsMatch(msg, "^GET")) {
                    Byte[] response = WSMessage.ComposeHandshakeResponse(msg);
                    stream.Write(response, 0, response.Length);
                    this.handshake_done = true;
                    wsmsg.was_handshake = true;
                    return wsmsg;
                }
            }

            //Decode the incoming message and return it
            wsmsg.Decode(buffer);
            return wsmsg;
        }

        /*
         *  Sends the data in the given WSMessage object to the client
         */
        private void sendMessage(WSMessage message) {
            this.stream.Write(message.GetEncodedMessage(), 0, message.GetEncodedMessage().Length);
        }

        /*
         *  Verifies if the given string the following format (example): 32,1681593747,44.8159745,20.4601243
         */
        private Boolean VerifyData(String data) {
            String pattern = @"^(\d{1}|\d{2}|\d{3}|\d{4}|\d{5}),\b[0-9]{10}\,\b[0-9]{2}\.\b[0-9]{7}\,\b[0-9]{2}\.\b[0-9]{7}";
            return Regex.IsMatch(data, pattern);
        }
    }
}
