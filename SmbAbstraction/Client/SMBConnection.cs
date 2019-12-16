﻿using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using SMBLibrary;
using SMBLibrary.Client;
using System.Threading;

namespace SmbAbstraction
{
    public class SMBConnection : IDisposable
    {
        private static Dictionary<int, Dictionary<IPAddress, SMBConnection>> instances = new Dictionary<int, Dictionary<IPAddress, SMBConnection>>();
        private static readonly object _connectionLock = new object();

        private readonly IPAddress _address;
        private readonly SMBTransportType _transport;
        private readonly ISMBCredential _credential;
        private long _referenceCount { get; set; }
        private bool _isDesposed { get; set; }
        private int _threadId { get; set; }

        public readonly ISMBClient SMBClient;

        private SMBConnection(ISMBClientFactory smbClientFactory, IPAddress address, SMBTransportType transport, ISMBCredential credential, int threadId)
        {
            SMBClient = smbClientFactory.CreateClient();
            _address = address;
            _transport = transport;
            _credential = credential;
            _referenceCount = 1;
            _threadId = threadId;
        }

        private void Connect()
        {
            var succeded = SMBClient.Connect(_address, _transport);
            if(!succeded)
            {
                throw new IOException($"Unable to connect to SMB share.");
            }
            var status = SMBClient.Login(_credential.Domain, _credential.UserName, _credential.Password);

            status.HandleStatus();
        }

        public static SMBConnection CreateSMBConnection(ISMBClientFactory smbClientFactory, IPAddress address, SMBTransportType transport, ISMBCredential credential)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;

            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            lock (_connectionLock)
            {
                if(!instances.ContainsKey(threadId))
                {
                    instances.Add(threadId, new Dictionary<IPAddress, SMBConnection>());
                }

                if (instances[threadId].ContainsKey(address))
                {
                    var instance = instances[threadId][address];
                    instance._referenceCount += 1;
                    return instance;
                }
                else
                {
                    var instance = new SMBConnection(smbClientFactory, address, transport, credential, threadId);
                    instance.Connect();
                    instances[threadId].Add(address, instance);
                    return instance;
                }
            }
        }

        public void Dispose()
        {
            lock (_connectionLock)
            {
                if (_isDesposed)
                {
                    return;
                }

                if (_referenceCount == 1)
                {
                    try
                    {
                        SMBClient.Logoff(); //Once you logout OR disconnect you can't log back in for some reason. TODO come back to this and try to debug more.
                        SMBClient.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    finally
                    {
                        instances[_threadId].Remove(_address);
                        if(instances[_threadId].Count == 0)
                        {
                            instances.Remove(_threadId);
                        }
                        _isDesposed = true;
                    }
                }
                else
                {
                    _referenceCount -= 1;
                }
            }
        }
    }
}
