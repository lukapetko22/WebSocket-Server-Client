# C# WebSocket server + Python parallel WebSocket GPS client simulator

  _Fully functional EXAMPLE WS server written in C# that supports multiple simultaneous clients and logs client GPS data in an SQLite DB_
# _Demo video:_
[![Demo video](https://img.youtube.com/vi/IxU_0D8ryTw/0.jpg)](https://www.youtube.com/watch?v=IxU_0D8ryTw)

# _Server:_
Server is capable of handling multiple clients at the same time by using a Task for each connection.

_Handling_ WebSocket messages is fully abstracted through the usage of WSMessage class:
- Message decoding - masked and unmasked
- Message encoding
- Handshaking
- Message control frame recognition - TEXT, PING, PONG, CLOSE 
- Control frame generating - TEXT, PING, PONG, CLOSE

    ``` cs
        WSMessage msg1 = new WSMessage();
        msg1.EncodeNoMask(Encoding.ASCII.GetBytes("This is a string."));
        Console.WriteLine(msg1.GetDecodedMessage());
        Console.WriteLine(Encoding.ASCII.GetString(msg1.GetEncodedMessage()));

        WSMessage msg2 = new WSMessage();
        msg2.Decode(msg1.GetEncodedMessage());
        Console.WriteLine(msg2.GetDecodedMessage());
    ```

_Communicating_ with clients is fully abstracted through the ClientHandler class and server can send data to clients:
- private WSMessage getMessage(int messagesize)
- private void sendMessage(WSMessage message)

_Storing GPS data_ in an SQLite database:
- SQLite has been chosen as it supports concurrency.
- Data is extracted from a TEXT frame:
    - Device ID
    - Device timestamp
    - Latitude
    - Longitude
- Data is sanitized before being stored by using REGEX

_Reusing tasks_ - Task completes upon receiving a CLOSE frame
_Pinging_ - Server is capable of responding to PING frames

# _Client:_
Client simulator is written in Python:
- Simulates n clients (args[1]) by utilizing multithreading
- Each client sends random GPS data on random intervals
    - After sending the data, client waits for the server response - "OK"
- After randomly picked number of messages sent, client closes the connection (server frees up the task dedicated to that client)
