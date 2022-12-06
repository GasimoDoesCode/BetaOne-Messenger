﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using Newtonsoft.Json;
using System.Net.Http;

namespace BetaOne
{
    internal class Server
    {

        int port;
        bool enableLogging = true;

        public Server(int port)
        {
            this.port = port;
        }

        public void init()
        {
            new Thread(listenForClients).Start();   
        }

        void listenForClients()
        {
            TcpListener tcpListener = new TcpListener(port);
            tcpListener.Start();
            ServerLogger.logServerInfo("Starting server on " + port);

            // Check for new clients here
            while (true)
            {
                TcpClient tcpClient = tcpListener.AcceptTcpClientAsync().Result;
                new Thread(() => RunClient(tcpClient)).Start();
            }

        }

        public void RunClient(TcpClient tcpClient)
        {
            ServerLogger.logTraffic("Connected.", tcpClient.Client.RemoteEndPoint.ToString().Split(":")[0], "server");
            var reader = new StreamReader(tcpClient.GetStream());
            var writer = new StreamWriter(tcpClient.GetStream());
            tcpClient.NoDelay = true;

            User u = new User();
            u.client = tcpClient;

            // SEND IDENT()
            Command request = new Command("ident", null, ReturnCodes.IDENT_REQUESTED);
            ServerLogger.logTraffic(request, "Server(Direct)", tcpClient.Client.RemoteEndPoint.ToString().Split(":")[0]);
            writer.WriteLineAsync(serializeCommand(request)).Wait();
            writer.Flush();
            

            // sendToClient(new Command("ident", null, ReturnCodes.IDENT_REQUESTED), tcpClient).Wait();

            // MUST IDENT
            while (u.username == null)
            {
                // RECEIVE IDENT
                var line = reader.ReadLineAsync().Result;

                // PROCESS
                commandHandler(commandParser(line), u);
            
            }

            ServerLogger.logServerInfo("User authorized " + u.username);

            // CLIENT LOOP
            while (true)
            {
                // RECEIVE
                var line = reader.ReadLineAsync().Result;

                // PROCESS
                new Thread(() => commandHandler(commandParser(line), u)).Start();
            }
        }


        /// <summary>
        /// Sends command to client
        /// </summary>
        /// <param name="cmd"></param>
        async Task sendToClient(Command cmd, TcpClient tcpClient)
        {
            ServerLogger.logTraffic(cmd, "Server(Direct)", tcpClient.Client.RemoteEndPoint.ToString().Split(":")[0]);

            StreamWriter sw = new StreamWriter(tcpClient.GetStream()); 
            await sw.WriteAsync((serializeCommand(cmd)));
            await sw.FlushAsync();
        }




        /*
         *  COMMAND HANDLER AREA
         */

        public string serializeCommand(Command cmd)
        {
            return JsonConvert.SerializeObject(cmd);
        }

        public Command commandParser(string json)
        {
            try 
            { 
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Command>(json);
            }
            catch (Exception e)
            {
                ServerLogger.LogError(e.Message);

#if DEBUG
                throw (e);
#endif

                return null;
            }

        }

        public void commandHandler(Command cmd, User user)
        {

            if (cmd == null)
                return;

            ServerLogger.logTraffic(cmd, user.client.Client.RemoteEndPoint.ToString().Split(":")[0], "server");

            switch (cmd.name)
            {
                case "ident":
                    {
                        user.username = cmd.content[0];
                        user.id = long.Parse(cmd.content[1]);

                        // WAS OK?
                        if(user.username != null && user.id != 0)
                        {
                            // SEND ALL OK
                            Command result = new Command("ident");
                            sendToClient(result, user.client).Wait();

                            Console.WriteLine("User authenticated: " + user.id + " " + user.username);

                            return;
                        }
                        else
                        {
                            // NOT OK
                            Command result = new Command("ident");
                            result.code = ReturnCodes.BAD_DATA;
                            sendToClient(result, user.client).Wait();
                            return;
                        }   
                    }

                case "register":
                    {
                        // Bad length
                        if(cmd.content.Length < 2)
                        {
                            sendToClient(new Command("ident", null, ReturnCodes.BAD_DATA), user.client).Wait();
                            return;
                        }

                        user.username = cmd.content[0];
                        user.id = 101010;

                        // Send handle back
                        sendToClient(new Command("register", new string[] {user.username, user.id.ToString()}, ReturnCodes.OK), user.client).Wait();

                        return;
                    }

                default:
                    {
                        sendToClient(new Command("result", null, ReturnCodes.BAD_REQUEST), user.client).Wait();
                        return;
                    }
            }
        }


    }
}