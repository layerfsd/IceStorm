﻿using Manager.Domain;
using Manager.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Manager.Server
{
    public class IceServer
    {
        private const string HANDSHAKE = "168494987!#())-=+_IceStorm153_))((--85*";

        private Logger log = Logger.Instance;

        private ManualResetEvent stopEvent;
        private Thread threadListen;
        private Socket listener;
        private SocketHelper soc;

        private string serverIp = "";
        private int port = 0;

        private object syncClients = new object();
        private Client[] clients;
        public Action<Client[]> ClientsChangedCallback { get; set; }

        public IceServer()
        {
            clients = new Client[0];
        }

        public void StartServer()
        {
            LoadConfig();
            threadListen = new Thread(ListenThread);
            stopEvent = new ManualResetEvent(false);
            soc = new SocketHelper(ref stopEvent);

            threadListen.Start();

            log.Info("Server started");
        }

        private void LoadConfig()
        {
            serverIp = "192.168.194.1";
            port = 12345;
        }
        
        private IPEndPoint GetIPEndPoint()
        {
            IPAddress ipAddress = null;// ipHostInfo.AddressList[0];
            foreach (IPAddress ipAddr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ipAddr.ToString().Equals(serverIp))
                {
                    ipAddress = ipAddr;
                    break;
                }
            }

            return new IPEndPoint(ipAddress, port);
        }
        public void StopServer()
        {
            stopEvent.Set();

            threadListen.Join(5000);
            threadListen = null;

            log.Info("Server stopped");
        }

        private void ListenThread()
        {
            if (stopEvent.WaitOne(1000)) return;

            log.Info("ListenThread started");
            int errors = 0;
            bool restartServer = false;
            
            if (!CreateSocket()) return;

            log.Info("Waiting for clients...");
            while (true)
            {
                try
                {
                    Socket clientSocket = null;
                    
                    try
                    {
                        clientSocket = listener.Accept();
                    }
                    catch (SocketException ex)
                    {
                        if (ex.ErrorCode != 10035)
                        {
                            log.Error(ex.Message);
                            throw;
                        }
                    }
                    
                    if (null == clientSocket)
                    {
                        if (stopEvent.WaitOne(200)) break;
                        continue;
                    }

                    if (stopEvent.WaitOne(1)) break;
                    //clientSocket.RemoteEndPoint
                    log.Info(string.Format("Client connected"));

                    clientSocket.Blocking = false;

                    int idx = 0;
                    lock (syncClients)
                    {
                        idx = clients.Length;
                        Array.Resize(ref clients, idx + 1);
                        clients[idx] = new Client();
                        clients[idx].Socket = clientSocket;
                        clients[idx].IP = clients[idx].Socket.RemoteEndPoint.ToString();

                        if (!GetHanshakePackege(clients[idx]))
                        {
                            clients[idx].Socket.Shutdown(SocketShutdown.Both);
                            clients[idx].Socket.Close();
                        }
                        else
                        {
                            clients[idx].Name = soc.RecvString(clients[idx].Socket);
                            clients[idx].OS = soc.RecvString(clients[idx].Socket);
                            clients[idx].Platform = soc.RecvString(clients[idx].Socket);
                            clients[idx].NrOfProcessors = soc.RecvDWORD(clients[idx].Socket);

                            NotifyClientsChange();
                        }
                    }

                    errors = 0;
                }
                catch (Exception ex)
                {
                    log.Error("Exception in listen loop: " + ex.Message);
                    errors++;

                    if (errors == 10)
                    {
                        restartServer = true;
                        break;
                    }
                }
                
                if (stopEvent.WaitOne(100)) break;
                log.Info("nu");
            }

            if (restartServer && !stopEvent.WaitOne(50))
            {
                log.Info("Will restart the server");
                StartServer();
            }

            log.Info("ListenThread stopped");
        }

        private bool GetHanshakePackege(Client client)
        {
            int result = 1;
            string handshakePacket = soc.RecvString(client.Socket);

            if (!HANDSHAKE.Equals(handshakePacket)) result = 0;
            
            log.Info(string.Format("Client {0} sent {1} handshake.", client.IP, result == 1 ? "good" : "bad"));

            soc.SendDWORD(client.Socket, result);

            return result == 1;
        }

        private bool CreateSocket()
        {
            while (true)
            {
                try
                {
                    IPEndPoint localEndPoint = GetIPEndPoint();
                    listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    listener.Blocking = false;
                    listener.Bind(localEndPoint);
                    listener.Listen(100);

                    log.Info("Listen socket created with success");
                    return true;
                }
                catch (Exception ex)
                {
                    log.Error(ex.Message);
                }

                if (stopEvent.WaitOne(200)) return false;
            }
        }

        public Client[] GetClients()
        {
            return clients;
        }

        public AppCtrlRule[] GetAppCtrlRules(Client client)
        {
            if (null != client.AppCtrlRules) return client.AppCtrlRules;

            AppCtrlRule[] fakeAppRules = new AppCtrlRule[10];

            for (int i = 0; i < fakeAppRules.Length; i++)
            {
                fakeAppRules[i] = new AppCtrlRule();

                fakeAppRules[i].RuleID = i + 1;
                fakeAppRules[i].ProcessPath = "C:\\path\\proces" + i + ".exe";
                fakeAppRules[i].ParentPath = "C:\\path\\parinte" + i + ".exe";
                fakeAppRules[i].ParentPID = (i + 1) * 50 + i;
                fakeAppRules[i].PID = (i + 1) * 50;
                fakeAppRules[i].ProcessPathMatcher = ((i % 2) == 1) ? IceStringMatcher.Equal : IceStringMatcher.Wildmat;
                fakeAppRules[i].ParentPathMatcher = ((i % 2) == 1) ? IceStringMatcher.Equal : IceStringMatcher.Wildmat;
                fakeAppRules[i].AddTime = 6000 + i;
                fakeAppRules[i].Verdict = ((i % 2) == 1) ? IceScanVerdict.Allow : IceScanVerdict.Deny;
            }

            client.AppCtrlRules = fakeAppRules;
            return fakeAppRules;
        }

        public FSRule[] GetFSRules(Client client)
        {
            if (null != client.FSRules) return client.FSRules;

            FSRule[] fakeFSRules = new FSRule[10];

            for (int i = 0; i < fakeFSRules.Length; i++)
            {
                fakeFSRules[i] = new FSRule();

                fakeFSRules[i].RuleID = i + 1;
                fakeFSRules[i].ProcessPathMatcher = ((i % 2) == 1) ? IceStringMatcher.Equal : IceStringMatcher.Wildmat;
                fakeFSRules[i].ProcessPath = "C:\\path\\proces" + i + ".exe";
                fakeFSRules[i].PID = (i + 1) * 20;
                fakeFSRules[i].FilePathMatcher = ((i % 2) == 1) ? IceStringMatcher.Equal : IceStringMatcher.Wildmat;
                fakeFSRules[i].FilePath = "C:\\fisiere\\file" + i + ".txt";
                fakeFSRules[i].DeniedOperations = i;
                fakeFSRules[i].AddTime = 5000 + i;
            }

            client.FSRules = fakeFSRules;
            return fakeFSRules;
        }

        public AppCtrlEvent[] GetAppCtrlEvents(Client client)
        {
            if (null != client.AppCtrlEvents) return client.AppCtrlEvents;

            AppCtrlEvent[] fakeAppEvents = new AppCtrlEvent[10];

            for (int i = 0; i < fakeAppEvents.Length; i++)
            {
                fakeAppEvents[i] = new AppCtrlEvent();

                fakeAppEvents[i].EventID = i + 1;
                fakeAppEvents[i].ProcessPath = "C:\\path\\proces" + i + ".exe";
                fakeAppEvents[i].PID = (i + 1) * 50;
                fakeAppEvents[i].ParentPath = "C:\\path\\parinte" + i + ".exe";
                fakeAppEvents[i].ParentPID = (i + 1) * 50 + i;
                fakeAppEvents[i].Verdict = ((i % 2) == 1) ? IceScanVerdict.Allow : IceScanVerdict.Deny;
                fakeAppEvents[i].MatchedRuleID = i * 2 +1;
                fakeAppEvents[i].EventTime = 6000 + i;
            }

            client.AppCtrlEvents = fakeAppEvents;
            return fakeAppEvents;
        }

        public FSEvent[] GetFSEvents(Client client)
        {
            if (null != client.FSEvents) return client.FSEvents;

            FSEvent[] fakeFSEvents = new FSEvent[10];

            for (int i = 0; i < fakeFSEvents.Length; i++)
            {
                fakeFSEvents[i] = new FSEvent();

                fakeFSEvents[i].EventID = i + 1;
                fakeFSEvents[i].ProcessPath = "C:\\path\\proces" + i + ".exe";
                fakeFSEvents[i].PID = (i + 1) * 20;
                fakeFSEvents[i].FilePath = "C:\\fisiere\\file" + i + ".txt";
                fakeFSEvents[i].RequiredOperations = i + 10;
                fakeFSEvents[i].DeniedOperations = i + 10 - 5;
                fakeFSEvents[i].RequiredOperations = i;
                fakeFSEvents[i].MatchedRuleID = i * 2 + 1;
                fakeFSEvents[i].EventTime = 7000 + i;
            }

            client.FSEvents = fakeFSEvents;
            return fakeFSEvents;
        }

        public int GetAppCtrlStatus(Client client)
        {
            return client.IsAppCtrlEnabled ? 1 : 0;
        }

        public int EnableAppCtrl(Client client, int enable)
        {
            client.IsAppCtrlEnabled = enable == 1;
            return 1;
        }

        public int EnableFSScan(Client client, int enable)
        {
            client.IsFSScanEnabled = enable == 1;
            return 1;
        }

        public int GetFSScanStatus(Client client)
        {
            return client.IsFSScanEnabled ? 1 : 0;
        }

        public int SendSetOption(Client client, int option, int value)
        {
            log.Info("Setoption " + option + " " + value);

            return 1;
        }

        public int DeleteFSScanRule(Client client, int id)
        {
            client.FSRules = client.FSRules.Where(rule => rule.RuleID != id).ToArray();
            return 1;
        }
        public int AddAppCtrlRule(Client client, AppCtrlRule rule)
        {
            rule.RuleID = client.AppCtrlRules.Length + 1;
            AppCtrlRule[] rules = client.AppCtrlRules;
            int len = client.AppCtrlRules.Length;

            Array.Resize(ref rules, rules.Length + 1);
            rules[len] = rule;

            client.AppCtrlRules = rules;
            return rule.RuleID;
        }

        public int AddFSScanRule(Client client, FSRule rule)
        {
            rule.RuleID = client.FSRules.Length + 1;
            FSRule[] rules = client.FSRules;
            int len = client.FSRules.Length;

            Array.Resize(ref rules, rules.Length + 1);
            rules[len] = rule;

            client.FSRules = rules;
            return rule.RuleID;
        }

        public int DeleteAppCtrlRule(Client client, int id)
        {
            client.AppCtrlRules = client.AppCtrlRules.Where(rule => rule.RuleID != id).ToArray();
            return 1;
        }

        public int UpdateAppCtrlRule(Client client, AppCtrlRule rule)
        {
            for (int i = 0; i < clients.Length; i++)
            {
                if (clients[i].ClientID == client.ClientID)
                {
                    for (int j = 0; j < clients[i].AppCtrlRules.Length; j++)
                    {
                        if (clients[i].AppCtrlRules[j].RuleID == rule.RuleID)
                        {
                            clients[i].AppCtrlRules[j].ProcessPathMatcher = rule.ProcessPathMatcher;
                            clients[i].AppCtrlRules[j].ProcessPath = rule.ProcessPath;
                            clients[i].AppCtrlRules[j].PID = rule.PID;
                            clients[i].AppCtrlRules[j].ParentPathMatcher = rule.ParentPathMatcher;
                            clients[i].AppCtrlRules[j].ParentPath = rule.ParentPath;
                            clients[i].AppCtrlRules[j].ParentPID = rule.ParentPID;
                            clients[i].AppCtrlRules[j].Verdict = rule.Verdict;

                            break;
                        }
                    }

                    break;
                }
            }
            
            return 1;
        }

        public int UpdateFSScanRule(Client client, FSRule rule)
        {
            for (int i = 0; i < clients.Length; i++)
            {
                if (clients[i].ClientID == client.ClientID)
                {
                    for (int j = 0; j < clients[i].FSRules.Length; j++)
                    {
                        if (clients[i].FSRules[j].RuleID == rule.RuleID)
                        {
                            clients[i].FSRules[j].ProcessPathMatcher = rule.ProcessPathMatcher;
                            clients[i].FSRules[j].ProcessPath = rule.ProcessPath;
                            clients[i].FSRules[j].PID = rule.PID;
                            clients[i].FSRules[j].FilePathMatcher = rule.FilePathMatcher;
                            clients[i].FSRules[j].FilePath = rule.FilePath;
                            clients[i].FSRules[j].DeniedOperations = rule.DeniedOperations;

                            break;
                        }
                    }

                    break;
                }
            }

            return 1;
        }
        private void NotifyClientsChange()
        {
            if (null == ClientsChangedCallback) return;

            ClientsChangedCallback(clients);
        }
    }
}
