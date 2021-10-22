using UnityEngine;
using System.Collections;
using System;
using SPacket.SocketInstance;
using System.Threading;
using Google.ProtocolBuffers;

public class NetWorkLogic_ : MonoBehaviour {

    public const uint SOCKET_ERROR = 0xFFFFFFFF;
    public const int PACKET_HEADER_SIZE = (sizeof(UInt32) + sizeof(UInt16));
    public const int MAX_ONE_PACKET_BYTE_SIZE = 1024; //单个收包，默认1K缓存
    public const int EACHFRAME_PROCESSPACKET_COUNT = 12;

    public string GetIP() { return m_strServerAddr; }
    public int GetPort() { return m_nServerPort; }

    public delegate void ConnectDelegate(bool bSuccess, string result);
    private static ConnectDelegate m_delConnect = null;

    public delegate void ConnectLostDelegate();
    private static ConnectLostDelegate m_delConnectLost = null;//断线重连
    public static NetWorkLogic_ GetMe()
    {
        if (m_Impl == null)
        {
            m_Impl = new NetWorkLogic_();
        }
        return m_Impl;
    }
    private static NetWorkLogic_ m_Impl = null;

    //收发消息包状态
    private bool m_bCanProcessPacket = true;
    public bool CanProcessPacket
    {
        get { return m_bCanProcessPacket; }
        set { m_bCanProcessPacket = value; }
    }

    //收发包流量统计
    public static int s_nReceiveCount = 0;
    public static int s_nSendCount = 0;

    string m_strServerAddr;
    int m_nServerPort;
    int m_nConnectSleep;
    SocketInstance m_Socket; //Socket
   // PacketFactoryManagerInstance m_PacketFactoryManager; // 项目中的pb工厂
    SocketInputStream m_SocketInputStream; // 接收消息
    SocketOutputStream m_SocketOutputStream; //发送消息
    Byte m_nPacketIndex = 123;

    float m_timeConnectBegin;

    //发包缓存
    private byte[] m_SendbyteData;
    private byte[] m_LenbyteData;
    private byte[] m_PacketIDbyteData;

    //收包缓存
    private int m_MaxRevOnePacketbyteCount;
    private byte[] m_MaxRevOnePacketbyte;

    //包头缓存
    private byte[] m_HeadbyteData;

    //每帧处理包的数量上限
    private int m_nEachFrame_ProcessPacket_Count;

    Thread m_hConnectThread = null;

    //初始化
    private NetWorkLogic_()
    {
        m_Socket = new SocketInstance();
       // m_PacketFactoryManager = new PacketFactoryManagerInstance();
        m_hConnectThread = null;
       //  m_PacketFactoryManager.Init();
        m_SendbyteData = new byte[SocketOutputStream.DEFAULT_SOCKET_OUTPUT_BUFFER_SIZE]; //8K
        m_LenbyteData = new byte[sizeof(Int32)];
        m_PacketIDbyteData = new byte[sizeof(Int16)];
        m_MaxRevOnePacketbyteCount = MAX_ONE_PACKET_BYTE_SIZE;
        m_MaxRevOnePacketbyte = new byte[m_MaxRevOnePacketbyteCount];
        m_HeadbyteData = new byte[PACKET_HEADER_SIZE];
        m_nEachFrame_ProcessPacket_Count = EACHFRAME_PROCESSPACKET_COUNT;
    }
    public enum ConnectStatus
    {
        INVALID,
        CONNECTING,    //连接中
        CONNECTED,     //已连接
        DISCONNECTED   //已断连
    }

    protected ConnectStatus m_connectStatus = ConnectStatus.INVALID;
    private bool m_bConnectFinish = false;
    private string m_strConnectResult = "";

    public bool IsDisconnected() { return m_connectStatus == ConnectStatus.DISCONNECTED; }
    public void WaitConnected() { m_connectStatus = ConnectStatus.CONNECTING; }
    public ConnectStatus GetConnectStautus() { return m_connectStatus; }

    //设置回掉方法
    public static void SetConcnectDelegate(ConnectDelegate delConnectFun)
    {
        m_delConnect = delConnectFun;
    }

    public static void SetConnectLostDelegate(ConnectLostDelegate delFun) // 设置断线重连
    {
        m_delConnectLost = delFun;
    }

    public void Update()
    {
        if (m_bConnectFinish)
        {
            m_bConnectFinish = false;
            if (null != m_delConnect)
            {
                m_delConnect(m_connectStatus == ConnectStatus.CONNECTED, m_strConnectResult);
            }

        }

        WaitPacket();
    }

    void Reset()
    {
        DisconnectServer();
    }
    //断开连接
    public void DisconnectServer()
    {
        if (m_strServerAddr == null || m_strServerAddr.Length == 0) return;

        m_Socket.close();
        m_connectStatus = ConnectStatus.DISCONNECTED;
    }

    //执行断线重连
    void ConnectLost()
    {
        m_connectStatus = ConnectStatus.DISCONNECTED;
        if (null != m_delConnectLost) m_delConnectLost();
    }
    //析构函数
    ~NetWorkLogic_()
    {
        DisconnectServer();
    }


    public bool IsCryptoPacket(UInt16 nPacketID)
    {
        return (nPacketID != (UInt16)MessageID.PACKET_CG_LOGIN &&
                nPacketID != (UInt16)MessageID.PACKET_GC_LOGIN_RET &&
                nPacketID != (UInt16)MessageID.PACKET_CG_CONNECTED_HEARTBEAT &&
                nPacketID != (UInt16)MessageID.PACKET_GC_CONNECTED_HEARTBEAT);
    }

    void WaitPacket()
    {
        try
        {
            if (!m_Socket.IsValid)
            {
                return;
            }
            if (!m_Socket.IsConnected)
            {
                return;
            }
            if (!ProcessInput())//接收发过来的消息，但是不处理，缓存起来
            {
                return;
            }
            if (!ProcessOutput()) //发送消息
            {
                return;
            }

            ProcessPacket();//处理接受的消息
        }
        catch (System.Exception ex)
        {
          
           //可以输出报错
        }
    }

    //接收消息
    bool ProcessInput()
    {
        if (m_SocketInputStream == null)
        {
            return false;
        }
        if (m_Socket.IsCanReceive() == false)
        {
            return true;
        }

        uint nSizeBefore = m_SocketInputStream.Length();
        uint ret = m_SocketInputStream.Fill();//在这里缓存
        uint nSizeAfter = m_SocketInputStream.Length();
        if (ret == SOCKET_ERROR)
        {
           // LogModule.WarningLog("send packet fail");
            m_Socket.close();
            ConnectLost();
            return false;
        }

        //收包统计
        if (nSizeAfter > nSizeBefore)
        {
            if (NetWorkLogic_.s_nReceiveCount < 0)
            {
                NetWorkLogic_.s_nReceiveCount = 0;
            }
            NetWorkLogic_.s_nReceiveCount += (int)(nSizeAfter - nSizeBefore);
        }
        return true;
    }

    //发送消息
    bool ProcessOutput()
    {
        if (m_SocketOutputStream == null)
        {
            return false;
        }
        if (m_Socket.IsCanSend() == false)
        {
            return true;
        }

        uint ret = m_SocketOutputStream.Flush();//在这里发送
        if (ret == SOCKET_ERROR)
        {
          //  LogModule.WarningLog("send packet fail");
            m_Socket.close();
            ConnectLost();
            return false;
        }

        return true;
    }


    //处理接收的消息
    void ProcessPacket()
    {
        if (m_SocketInputStream == null)
        {
            return;
        }

        int nProcessPacketCount = m_nEachFrame_ProcessPacket_Count;

        Int32 packetSize;
        Int16 messageid;
        Ipacket pPacket = null;
        while (m_bCanProcessPacket && (nProcessPacketCount--) > 0)
        {
            Array.Clear(m_HeadbyteData, 0, PACKET_HEADER_SIZE);
            if (!m_SocketInputStream.Peek(m_HeadbyteData, PACKET_HEADER_SIZE))
            {
                break;
            }

            packetSize = BitConverter.ToInt32(m_HeadbyteData, 0);
            packetSize = System.Net.IPAddress.NetworkToHostOrder(packetSize) + 4;

            messageid = BitConverter.ToInt16(m_HeadbyteData, sizeof(UInt32));
            messageid = System.Net.IPAddress.NetworkToHostOrder(messageid);


            if (m_SocketInputStream.Length() < packetSize)
            {
                break;
            }

            try
            {
                if (m_MaxRevOnePacketbyteCount < packetSize)
                {
                    m_MaxRevOnePacketbyte = new byte[packetSize];
                    m_MaxRevOnePacketbyteCount = packetSize;
                }
                Array.Clear(m_MaxRevOnePacketbyte, 0, m_MaxRevOnePacketbyteCount);

                bool bRet = m_SocketInputStream.Skip(PACKET_HEADER_SIZE);
                if (bRet == false)
                {
                    string errorLog = string.Format("Can not Create Packet MessageID({0},packetSize{1})", messageid, packetSize);
                    throw PacketException.PacketReadError(errorLog);
                }
                m_SocketInputStream.Read(m_MaxRevOnePacketbyte, (uint)(packetSize - PACKET_HEADER_SIZE));
                //if (IsCryptoPacket((UInt16)messageid))
                //{
                //    XorCrypto.XorDecrypt(m_MaxRevOnePacketbyte, (uint)(packetSize - PACKET_HEADER_SIZE));
                //}

                // pPacket = m_PacketFactoryManager.GetPacketHandler((MessageID)messageid);
                // NGUIDebug.Log("ReceivePacket:" + ((MessageID)messageid).ToString());
                //NGUIDebug.Log("PacketSize:" + (packetSize));
                //if (pPacket == null)
                //{
                //    string errorLog = string.Format("Can not Create Packet MessageID({0},buff{1})", messageid, LogModule.ByteToString(m_MaxRevOnePacketbyte, 0, m_MaxRevOnePacketbyteCount));
                //    throw PacketException.PacketCreateError(errorLog);
                //}
                #region 在这里处理消息
                //PacketDistributed realPacket = PacketDistributed.CreatePacket((MessageID)messageid);

                //if (realPacket == null)
                //{
                //    string errorLog = string.Format("Can not Create Inner Packet Data MessageID({0},buff{1})", messageid, LogModule.ByteToString(m_MaxRevOnePacketbyte, 0, m_MaxRevOnePacketbyteCount));
                //    throw PacketException.PacketCreateError(errorLog);
                //}
                //PacketDistributed instancePacket = realPacket.ParseFrom(m_MaxRevOnePacketbyte, packetSize - PACKET_HEADER_SIZE);
                //if (instancePacket == null)
                //{
                //    string errorLog = string.Format("Can not Merged Inner Packet Data MessageID({0},buff{1})", messageid, LogModule.ByteToString(m_MaxRevOnePacketbyte, 0, m_MaxRevOnePacketbyteCount));
                //    throw PacketException.PacketCreateError(errorLog);
                //}

                //执行消息
                //uint result = pPacket.Execute(instancePacket);
             
                //=====发包成功
                //EventManager.instance.dispatchEvent(new Hashtable(),"ReceiveHeartPingPong");
                //=======


                //判断消息
                //if ((PACKET_EXE)result != PACKET_EXE.PACKET_EXE_NOTREMOVE)
                //{
                //    m_PacketFactoryManager.RemovePacket(pPacket);
                   
                //}
                //else if ((PACKET_EXE)result == PACKET_EXE.PACKET_EXE_ERROR)
                //{
                //    string errorLog = string.Format("Execute Packet error!!! MessageID({0},buff{1})", messageid, LogModule.ByteToString(m_MaxRevOnePacketbyte, 0, m_MaxRevOnePacketbyteCount));
                //    throw PacketException.PacketExecuteError(errorLog);
                //}

                #endregion

            }
            catch (PacketException ex)
            {
                //LogModule.ErrorLog(ex.ToString());
                //可输出报错
            }
            catch (System.Exception ex)
            {
                // LogModule.ErrorLog(ex.ToString());
                //可输出报错
            }


        }

        if (nProcessPacketCount >= 0)
        {
            m_nEachFrame_ProcessPacket_Count = EACHFRAME_PROCESSPACKET_COUNT;
        }
        else
        {
            m_nEachFrame_ProcessPacket_Count += 4;
        }
    }

    //发送消息
    public void SendPacket(PacketDistributed pPacket)//参数为发送的消息
    {
        if (pPacket == null)
        {
            return;
        }
       
        //判断是否掉线
        if (m_connectStatus != ConnectStatus.CONNECTED)
        {
            if (m_connectStatus == ConnectStatus.DISCONNECTED)
            {
                // 再次询问断线重连情况
                ConnectLost();
            }
            return;
        }

        //=====发包成功
        //EventManager.instance.dispatchEvent(new Hashtable(),"StartHeartPingPong");
        //=======

        //判断是否在连接
        if (m_Socket.IsValid)
        {
            //判断消息内容
            if (!pPacket.IsInitialized())
            {
                throw InvalidProtocolBufferException.ErrorMsg("Request data have not set");
            }

            //获取消息长度
            int nValidbyteSize = pPacket.SerializedSize();
           // int nValidbyteSize = 0;

            if (nValidbyteSize <= 0)
            {
                return;
            }

            //Array.Clear(m_SendbyteData, 0, (int)SocketOutputStream.DEFAULT_SOCKET_OUTPUT_BUFFER_SIZE); 不用全部清理，用多少清多少吧
            int nClearCount = nValidbyteSize + 128;
            if (nClearCount > (int)SocketOutputStream.DEFAULT_SOCKET_OUTPUT_BUFFER_SIZE)
            {
                nClearCount = (int)SocketOutputStream.DEFAULT_SOCKET_OUTPUT_BUFFER_SIZE;
            }
            Array.Clear(m_SendbyteData, 0, nClearCount);
            CodedOutputStream output = CodedOutputStream.CreateInstance(m_SendbyteData, 0, nValidbyteSize);
            pPacket.WriteTo(output);
            output.CheckNoSpaceLeft();

            Int32 nlen = nValidbyteSize + NetWorkLogic.PACKET_HEADER_SIZE - 4;
            Int32 netnlen = System.Net.IPAddress.HostToNetworkOrder(nlen);
            Int16 messageid = System.Net.IPAddress.HostToNetworkOrder((Int16)pPacket.GetPacketID());
            //NGUIDebug.Log("PacketSize:"+(nlen+4));
            Array.Clear(m_LenbyteData, 0, sizeof(Int32));
            Array.Clear(m_PacketIDbyteData, 0, sizeof(Int16));

            m_LenbyteData[0] = (byte)(netnlen);  //小端顺序放
            m_LenbyteData[1] = (byte)(netnlen >> 8);
            m_LenbyteData[2] = (byte)(netnlen >> 16);
            m_LenbyteData[3] = (byte)(netnlen >> 24);


            m_PacketIDbyteData[0] = (byte)(messageid);
            m_PacketIDbyteData[1] = (byte)(messageid >> 8);

            uint nSizeBefore = m_SocketOutputStream.Length();
            m_SocketOutputStream.Write(m_LenbyteData, sizeof(Int32));
            m_SocketOutputStream.Write(m_PacketIDbyteData, sizeof(Int16));
            //if (IsCryptoPacket((UInt16)pPacket.GetPacketID()))
            //{
            //    XorCrypto.XorEncrypt(m_SendbyteData, (uint)nValidbyteSize);
            //}
            m_SocketOutputStream.Write(m_SendbyteData, (uint)nValidbyteSize);
            uint nSizeAfter = m_SocketOutputStream.Length();

            //发包统计
            if (nSizeAfter > nSizeBefore)
            {
                if (NetWorkLogic.s_nSendCount < 0)
                {
                    NetWorkLogic.s_nSendCount = 0;
                }
                NetWorkLogic.s_nSendCount += (int)(nSizeAfter - nSizeBefore);
            }
        }
        else
        {
            ConnectLost();
        }
    }

    //连接服务器
    public void ConnectToServer(string szServerAddr, int nServerPort, int nSleepTime)
    {
        if (m_connectStatus == ConnectStatus.CONNECTING)
        {
            return;
        }

        m_strServerAddr = szServerAddr;
        m_nServerPort = nServerPort;
        m_nConnectSleep = nSleepTime;

        m_hConnectThread = new Thread(new ParameterizedThreadStart(_ConnectThread));
        m_timeConnectBegin = Time.time;
        m_hConnectThread.Start(this);
    }

    protected static void _ConnectThread(object me)
    {
        NetWorkLogic rMe = me as NetWorkLogic;
        rMe.ConnectThread();
    }

}
