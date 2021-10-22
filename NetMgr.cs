using UnityEngine;
using System.Collections;

//脚本中的空方法为一部分登录逻辑方法，根据需求更改和添加
public class NetMgr : MonoBehaviour
{

    //单例
    static public NetMgr m_netManager;
    private static NetMgr m_instance = null;
    public static NetMgr Instance()
    {
        return m_instance;
    }

    //判断是否正处于询问断线状态
    private bool m_bAskConnecting = false;

    void Awake()
    {
        if (m_netManager != null)
        {
            Destroy(this.gameObject);
        }
        m_netManager = this;
        DontDestroyOnLoad(this.gameObject);
         NetWorkLogic.SetConnectLostDelegate(ConnectLost);
        m_instance = this;
    }

    //打开
    void OnEnable()
    {
        m_bAskConnecting = false;
    }

    void Update()
    {
         NetWorkLogic.GetMe().Update();
    }

    //连接服务器
    public void ConnectToServer(string _ip, int _port, NetWorkLogic.ConnectDelegate delConnect)
    {
        NetWorkLogic.SetConcnectDelegate(delConnect);
        NetWorkLogic.GetMe().ConnectToServer(_ip, _port, 100); // 链接服务器
    }

    //登录界面调用
    public static void SendUserLogin()//参数由需求定义
    {
        //此处写想服务器发送登录的消息，账号，密码等
    }


    //选择角色
    public static void SendChooseRole()//参数由需求定义
    {
        //此处为登录后选择角色发送的消息
    }

    //退出登录
    public static void SendUserLogout()
    {
        //发送退出登录的消息
    }


    //断线重连
    public void ConnectLost()
    {
        if (!GameManager.gameManager.OnLineState)
        {
            return;
        }
        //调回登录界面
        if (LoginUILogic.Instance() != null)
        {
            //重新打开登录界面，执行登录逻辑

        }
        //返回主界面
        else if (MainUILogic.Instance() != null)
        {
            if (!m_bAskConnecting)//
            {
                //重新打开主界面，执行主界面的逻辑
                m_bAskConnecting = true;

            }
        }
        else
        {
            // 有可能在loading不处理，等UI起来后检测
        }
    }

    //重新选服
    private static void OnReturnServerChoose()
    {
        //  NetWorkLogic.GetMe().DisconnectServer();
        
    }

    //重新连接
    public void OnReconnect()
    {
        m_bAskConnecting = false;
        NetWorkLogic.SetConcnectDelegate(Ret_Reconnect);
        NetWorkLogic.GetMe().ReConnectToServer();
    }

    void Ret_Reconnect(bool bSuccess, string strResult)
    {
        if (bSuccess)
        {
            // 重新登录
           
            
            //NetManager.SendUserLogin(PlayerPreferenceData.LastAccount, PlayerPreferenceData.LastPsw, Ret_Login);
        }
        else
        {
            // 重连失败，点击确定重新登录
           

        }
    }

    void OnChooseRole()
    {
        // 正在等待进入场景，选择角色
       
    }

    void EnterLoginScene()
    {
       // LoadingWindow.LoadScene(Games.GlobeDefine.GameDefine_Globe.SCENE_DEFINE.SCENE_LOGIN);
    }

    void EnterOffline()
    {
        //GameManager.gameManager.OnLineState = false;
    }

}
